import { chromium } from 'playwright'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import process from 'node:process'

const root = path.resolve(import.meta.dirname, '../..')
const toolRoot = path.join(root, 'tools', 'paldeck-sync')
const output = path.join(toolRoot, 'output')
const imageRoot = path.join(output, 'images')
const translationFile = path.join(toolRoot, 'translations.zh-CN.json')
const localItemCatalog = path.join(root, 'src', 'PalOps.Web', 'Seed', 'items.json')
const localPalCatalog = path.join(root, 'src', 'PalOps.Web', 'Seed', 'pals.json')

const sourceDefinitions = [
  { type: 'pal', slug: 'pals', linkPrefix: '/pals/', detailKind: 'pal' },
  { type: 'item', slug: 'items', linkPrefix: '/items/', detailKind: 'item' },
  { type: 'technology', slug: 'technology', listKind: 'cards' },
  { type: 'building', slug: 'buildings', linkPrefix: '/buildings/', detailKind: 'building' },
  { type: 'passive', slug: 'passives', listKind: 'headings' },
  { type: 'skill', slug: 'skills', listKind: 'headings' },
]

const options = parseArguments(process.argv.slice(2))
const selectedSources = sourceDefinitions.filter((source) => options.sources.size === 0 || options.sources.has(source.slug))
if (selectedSources.length === 0) {
  throw new Error(`没有匹配的数据源。可选值：${sourceDefinitions.map((source) => source.slug).join(', ')}`)
}

await mkdir(imageRoot, { recursive: true })
const translations = await loadTranslationIndex()
const browser = await chromium.launch({ headless: !options.headful })
const context = await browser.newContext({ locale: 'en-US' })
const manifest = {
  generatedAt: new Date().toISOString(),
  sourceHost: 'https://paldeck.cc',
  sources: [],
  notes: [
    'Paldeck 是非官方社区数据库。同步结果必须经过人工抽查后才能替换生产目录。',
    'nameZh 的来源会记录在 translationStatus；untranslated 表示没有伪造中文译名。',
    '应用运行时不访问 Paldeck；所有 JSON 与图片均为维护期离线同步产物。',
  ],
}

try {
  for (const source of selectedSources) {
    const sourceResult = await synchronizeSource(context, source, translations, options)
    manifest.sources.push(sourceResult.summary)
    await writeFile(
      path.join(output, `${source.slug}.json`),
      JSON.stringify(sourceResult.document, null, 2) + '\n',
      'utf8',
    )
  }
} finally {
  await browser.close()
}

await writeFile(path.join(output, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n', 'utf8')
console.log(`同步完成：${manifest.sources.map((item) => `${item.slug}=${item.count}`).join(', ')}`)

async function synchronizeSource(context, source, translationIndex, runOptions) {
  const page = await context.newPage()
  const sourceUrl = `https://paldeck.cc/${source.slug}`
  console.log(`Loading ${sourceUrl}`)

  try {
    await page.goto(sourceUrl, { waitUntil: 'domcontentloaded', timeout: 90_000 })
    await page.waitForTimeout(1_000)
    await exhaustInfiniteList(page)

    let entries = source.linkPrefix
      ? await extractLinkedListEntries(page, source)
      : await extractInlineEntries(page, source)

    if (source.detailKind) {
      const detailCandidates = runOptions.maxDetails > 0 ? entries.slice(0, runOptions.maxDetails) : entries
      const enriched = await mapWithConcurrency(detailCandidates, runOptions.detailConcurrency, async (entry) => {
        if (!entry.sourceUrl) return entry
        const detailPage = await context.newPage()
        try {
          await detailPage.goto(entry.sourceUrl, { waitUntil: 'domcontentloaded', timeout: 60_000 })
          await detailPage.waitForTimeout(250)
          return { ...entry, ...(await extractDetailMetadata(detailPage, source.detailKind)) }
        } catch (error) {
          return { ...entry, detailError: errorMessage(error) }
        } finally {
          await detailPage.close()
        }
      })
      entries = runOptions.maxDetails > 0
        ? [...enriched, ...entries.slice(runOptions.maxDetails)]
        : enriched
    }

    entries = deduplicateEntries(entries)
      .map((entry) => attachTranslation(entry, translationIndex))
      .sort(compareEntries)

    const sourceImageDirectory = path.join(imageRoot, source.slug)
    await mkdir(sourceImageDirectory, { recursive: true })
    if (!runOptions.skipImages) {
      await mapWithConcurrency(entries, runOptions.imageConcurrency, async (entry) => {
        await downloadImage(context, sourceImageDirectory, source.slug, entry)
        return entry
      })
    }

    const validation = validateEntries(entries)
    const document = {
      source: sourceUrl,
      scrapedAt: new Date().toISOString(),
      type: source.type,
      count: entries.length,
      validation,
      entries,
    }
    const summary = {
      slug: source.slug,
      type: source.type,
      count: entries.length,
      missingIds: validation.missingIds.length,
      untranslated: validation.untranslated.length,
      missingImages: validation.missingImages.length,
    }
    console.log(
      `${source.slug}: ${entries.length} records; missing ID ${summary.missingIds}; untranslated ${summary.untranslated}; missing image ${summary.missingImages}`,
    )
    return { document, summary }
  } finally {
    await page.close()
  }
}

async function extractLinkedListEntries(page, source) {
  return await page.evaluate(({ type, linkPrefix }) => {
    const clean = (value) => (value || '').replace(/\s+/g, ' ').trim()
    const samePath = (href) => {
      try {
        const url = new URL(href, location.origin)
        return url.origin === location.origin && url.pathname.startsWith(linkPrefix) && url.pathname !== linkPrefix
      } catch {
        return false
      }
    }
    const anchors = [...document.querySelectorAll('a[href]')].filter((anchor) => samePath(anchor.getAttribute('href') || ''))
    const seen = new Set()
    const entries = []

    for (const anchor of anchors) {
      const sourceUrl = new URL(anchor.getAttribute('href'), location.origin).href
      if (seen.has(sourceUrl)) continue
      seen.add(sourceUrl)
      const card = anchor.closest('article, li, tr, [class*="card"], [class*="row"], [class*="item"]') || anchor
      const image = card.querySelector('img') || anchor.querySelector('img')
      const heading = card.querySelector('h1,h2,h3,h4,strong')
      const slugName = decodeURIComponent(new URL(sourceUrl).pathname.split('/').filter(Boolean).at(-1) || '')
      const name = clean(image?.alt) || clean(heading?.textContent) || clean(anchor.textContent) || slugName
      const rawText = clean(card.textContent)
      const category = clean(card.querySelector('[data-category]')?.getAttribute('data-category')) || ''
      entries.push({
        type,
        id: '',
        nameEn: name,
        category,
        sourceUrl,
        imageUrl: image?.currentSrc || image?.src || '',
        rawText,
      })
    }
    return entries
  }, source)
}

async function extractInlineEntries(page, source) {
  return await page.evaluate(({ type, listKind }) => {
    const clean = (value) => (value || '').replace(/\s+/g, ' ').trim()
    const safeId = /^[A-Za-z][A-Za-z0-9_.:-]{1,127}$/
    const ignoredTokens = new Set([
      'rank', 'power', 'cooldown', 'category', 'structures', 'items', 'other', 'common', 'rare', 'epic', 'legendary',
    ])
    const pickId = (container, name) => {
      const explicit = [...container.querySelectorAll('[data-id], code')]
        .map((node) => clean(node.getAttribute?.('data-id') || node.textContent))
        .find((value) => safeId.test(value))
      if (explicit) return explicit
      const lines = (container.innerText || container.textContent || '').split(/\n+/).map(clean).filter(Boolean)
      const candidates = lines.filter((value) => safeId.test(value)
        && value.toLowerCase() !== name.toLowerCase()
        && !ignoredTokens.has(value.toLowerCase()))
      return candidates.find((value) => /[_:]/.test(value)) || candidates.at(-1) || ''
    }

    const containers = listKind === 'headings'
      ? [...document.querySelectorAll('main h3, main h4')].map((heading) => heading.closest('article, li, section, [class*="card"], [class*="row"]') || heading.parentElement)
      : [...document.querySelectorAll('main article, main li, main section, main [class*="card"], main [class*="row"]')]
    const seen = new Set()
    const result = []

    for (const container of containers.filter(Boolean)) {
      const heading = container.querySelector?.('h1,h2,h3,h4,strong')
      const image = container.querySelector?.('img')
      const name = clean(image?.alt) || clean(heading?.textContent)
      if (!name || name.length > 160) continue
      const id = pickId(container, name)
      const rawText = clean(container.textContent)
      const key = `${type}:${id || name}`.toLowerCase()
      if (seen.has(key)) continue
      seen.add(key)
      result.push({
        type,
        id,
        nameEn: name,
        category: '',
        sourceUrl: location.href,
        imageUrl: image?.currentSrc || image?.src || '',
        rawText,
      })
    }
    return result
  }, source)
}

async function extractDetailMetadata(page, detailKind) {
  return await page.evaluate((kind) => {
    const clean = (value) => (value || '').replace(/\s+/g, ' ').trim()
    const main = document.querySelector('main') || document.body
    const lines = (main.innerText || '').split(/\n+/).map(clean).filter(Boolean)
    const safeId = /^[A-Za-z][A-Za-z0-9_.:-]{1,127}$/
    const valueAfter = (label) => {
      const index = lines.findIndex((line) => line.toLowerCase() === label.toLowerCase())
      return index >= 0 ? lines[index + 1] || '' : ''
    }
    const heading = clean(main.querySelector('h1')?.textContent)
    const image = main.querySelector('img')
    const result = {
      nameEn: heading.replace(/^#\s*/, '').replace(/\s*\([^)]*\)\s*$/, '') || undefined,
      imageUrl: image?.currentSrc || image?.src || undefined,
      detailRawText: clean(main.textContent),
    }

    if (kind === 'item') {
      result.id = valueAfter('Asset Name')
      result.category = valueAfter('Category')
    } else if (kind === 'building') {
      const match = heading.match(/^(.*?)\s*\(([^)]+)\)\s*$/)
      result.nameEn = clean(match?.[1] || heading)
      result.id = clean(match?.[2] || '')
      result.blueprintClass = valueAfter('Blueprint Class')
      result.category = valueAfter('Type B') || valueAfter('Type A')
    } else if (kind === 'pal') {
      const headingIndex = lines.findIndex((line) => line === heading)
      const stopWords = new Set(['neutral', 'dark', 'grass', 'ground', 'water', 'fire', 'ice', 'electric', 'dragon', 'lore'])
      const candidates = lines.slice(Math.max(headingIndex + 1, 0), Math.max(headingIndex + 8, 8))
        .filter((line) => safeId.test(line) && !line.startsWith('#') && !stopWords.has(line.toLowerCase()))
      result.id = candidates[0] || ''
      result.dexNumber = lines.find((line) => /^#\d+[A-Z]?/.test(line))?.match(/^#(\d+[A-Z]?)/)?.[1] || ''
    }
    return result
  }, detailKind)
}

async function downloadImage(context, directory, slug, entry) {
  if (!entry.imageUrl) return
  try {
    const response = await context.request.get(entry.imageUrl, { timeout: 30_000 })
    if (!response.ok()) {
      entry.imageError = `HTTP ${response.status()}`
      return
    }
    const contentType = response.headers()['content-type'] || ''
    const extension = imageExtension(entry.imageUrl, contentType)
    const fileName = `${safeName(entry.id || entry.nameEn)}${extension}`
    const buffer = await response.body()
    await writeFile(path.join(directory, fileName), buffer)
    entry.localImage = `images/${slug}/${fileName}`
  } catch (error) {
    entry.imageError = errorMessage(error)
  }
}

async function loadTranslationIndex() {
  const index = {
    exact: new Map(),
    byId: new Map(),
    byEnglishName: new Map(),
  }

  for (const [type, file] of [['item', localItemCatalog], ['pal', localPalCatalog]]) {
    const entries = await readJson(file, [])
    for (const entry of entries) {
      if (entry.id && entry.nameZh) index.byId.set(`${type}:${entry.id}`.toLowerCase(), entry.nameZh)
      if (entry.nameEn && entry.nameZh) {
        const key = `${type}:${entry.nameEn}`.toLowerCase()
        if (!index.byEnglishName.has(key)) index.byEnglishName.set(key, entry.nameZh)
      }
    }
  }

  const manual = await readJson(translationFile, { entries: {} })
  for (const [key, value] of Object.entries(manual.entries || {})) {
    if (typeof value === 'string' && value.trim()) index.exact.set(key.toLowerCase(), value.trim())
  }
  return index
}

function attachTranslation(entry, index) {
  const exactKeys = [
    `${entry.type}:${entry.id || ''}`,
    `${entry.type}:${entry.nameEn || ''}`,
    entry.id || '',
    entry.nameEn || '',
  ].map((value) => value.toLowerCase()).filter(Boolean)

  for (const key of exactKeys) {
    const manual = index.exact.get(key)
    if (manual) return { ...entry, nameZh: manual, translationStatus: 'manual-override' }
  }

  const localById = index.byId.get(`${entry.type}:${entry.id || ''}`.toLowerCase())
  if (localById) return { ...entry, nameZh: localById, translationStatus: 'local-catalog-id' }

  const localByName = index.byEnglishName.get(`${entry.type}:${entry.nameEn || ''}`.toLowerCase())
  if (localByName) return { ...entry, nameZh: localByName, translationStatus: 'local-catalog-name' }

  const translated = translateByGlossary(entry.nameEn)
  if (translated) return { ...entry, nameZh: translated, translationStatus: 'glossary-assisted' }
  return { ...entry, nameZh: entry.nameEn, translationStatus: 'untranslated' }
}

const phraseTranslations = new Map(Object.entries({
  'Primitive Workbench': '原始工作台',
  'Stone Axe': '石斧',
  'Stone Pickaxe': '石镐',
  'Hand-Held Torch': '手持火把',
  'Wooden Club': '木棒',
  'Palbox': '帕鲁终端',
  'Campfire': '篝火',
  'Wooden Chest': '木制宝箱',
  'Repair Bench': '修理台',
  'Wooden Structure Set': '木制建筑套装',
  'Pal Dressing Facility': '帕鲁装扮设施',
  'Old Bow': '陈旧的弓',
  'Stone Spear': '石矛',
  'Shoddy Bed': '简陋的床',
  'Straw Pal Bed': '稻草帕鲁床',
  'Global Palbox': '全局帕鲁终端',
  'Common Shield': '普通护盾',
  'Cloth Outfit': '布衣',
  'Demon God': '魔神',
  'Legend': '传说',
  'Lucky': '稀有',
  'Swift': '神速',
  'Artisan': '工匠精神',
  'Burly Body': '顽强肉体',
  'Divine Dragon': '神龙',
  'Earth Emperor': '大地帝王',
  'Air Cannon': '空气炮',
  'Power Shot': '强力射击',
  'Power Bomb': '强力炸弹',
  'Pal Blast': '帕鲁光束',
  'Holy Burst': '圣光爆裂',
}))

const wordTranslations = new Map(Object.entries({
  ancient: '古代', advanced: '高级', air: '空气', ammo: '弹药', armor: '铠甲', axe: '斧',
  bed: '床', beam: '光束', body: '肉体', bow: '弓', box: '箱', building: '建筑', campfire: '篝火',
  cannon: '炮', cartridge: '弹匣', chest: '宝箱', cloth: '布', common: '普通', cooking: '烹饪',
  dark: '暗', defense: '防御', divine: '神圣', dragon: '龙', electric: '电气', emperor: '帝王',
  eternal: '永恒', facility: '设施', fire: '火', flame: '火焰', food: '食物', foundation: '地基',
  furnace: '熔炉', gun: '枪', handgun: '手枪', helmet: '头盔', holy: '圣光', ice: '冰',
  kitchen: '厨房', launcher: '发射器', legendary: '传说', machine: '机械', metal: '金属', mine: '地雷',
  mounted: '壁挂', old: '陈旧', outfit: '服装', pal: '帕鲁', pickaxe: '镐', plasma: '等离子',
  primitive: '原始', production: '生产', refined: '精炼', repair: '修理', rifle: '步枪', rocket: '火箭',
  shield: '护盾', shotgun: '霰弹枪', skill: '技能', spear: '矛', stone: '石', storage: '储存',
  structure: '建筑', sword: '剑', table: '桌', technology: '科技', torch: '火把', trap: '陷阱',
  water: '水', wooden: '木制', workbench: '工作台', world: '世界', tree: '树', speed: '速度',
  attack: '攻击', movement: '移动', work: '工作', sanity: 'SAN值', hunger: '饱腹度', max: '最大',
  health: '生命', stamina: '体力', lucky: '稀有', swift: '神速', artisan: '工匠',
}))

function translateByGlossary(name) {
  const normalized = String(name || '').trim()
  if (!normalized) return ''
  const phrase = phraseTranslations.get(normalized)
  if (phrase) return phrase
  const words = normalized.replace(/[()]/g, ' ').split(/[\s\-_/]+/).filter(Boolean)
  if (words.length === 0) return ''
  const translated = words.map((word) => wordTranslations.get(word.toLowerCase()))
  return translated.every(Boolean) ? translated.join('') : ''
}

function deduplicateEntries(entries) {
  const result = new Map()
  for (const entry of entries) {
    const key = `${entry.type}:${entry.id || entry.sourceUrl || entry.nameEn}`.toLowerCase()
    const current = result.get(key)
    if (!current || completenessScore(entry) >= completenessScore(current)) result.set(key, entry)
  }
  return [...result.values()]
}

function completenessScore(entry) {
  return ['id', 'nameEn', 'imageUrl', 'category', 'sourceUrl'].reduce((score, key) => score + (entry[key] ? 1 : 0), 0)
}

function validateEntries(entries) {
  return {
    missingIds: entries.filter((entry) => !entry.id).map((entry) => entry.nameEn),
    duplicateIds: duplicateValues(entries.map((entry) => entry.id).filter(Boolean).map((value) => value.toLowerCase())),
    untranslated: entries.filter((entry) => entry.translationStatus === 'untranslated').map((entry) => entry.id || entry.nameEn),
    missingImages: entries.filter((entry) => !entry.imageUrl).map((entry) => entry.id || entry.nameEn),
    imageErrors: entries.filter((entry) => entry.imageError).map((entry) => ({ id: entry.id || entry.nameEn, error: entry.imageError })),
  }
}

function duplicateValues(values) {
  const counts = new Map()
  for (const value of values) counts.set(value, (counts.get(value) || 0) + 1)
  return [...counts.entries()].filter(([, count]) => count > 1).map(([value]) => value).sort()
}

async function exhaustInfiniteList(page) {
  let stableRounds = 0
  let previousHeight = 0
  while (stableRounds < 5) {
    const height = await page.evaluate(() => document.body.scrollHeight)
    await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight))
    await page.waitForTimeout(850)
    if (height === previousHeight) stableRounds++
    else stableRounds = 0
    previousHeight = height
  }
  await page.evaluate(() => window.scrollTo(0, 0))
}

async function mapWithConcurrency(values, concurrency, worker) {
  const result = new Array(values.length)
  let index = 0
  const runners = Array.from({ length: Math.max(1, Math.min(concurrency, values.length || 1)) }, async () => {
    while (true) {
      const current = index++
      if (current >= values.length) return
      result[current] = await worker(values[current], current)
    }
  })
  await Promise.all(runners)
  return result
}

function compareEntries(left, right) {
  return String(left.id || left.nameEn).localeCompare(String(right.id || right.nameEn), 'en')
}

function imageExtension(url, contentType) {
  const fromUrl = path.extname(new URL(url).pathname).toLowerCase()
  if (/^\.(png|jpe?g|webp|gif|svg)$/.test(fromUrl)) return fromUrl
  if (contentType.includes('png')) return '.png'
  if (contentType.includes('jpeg')) return '.jpg'
  if (contentType.includes('gif')) return '.gif'
  if (contentType.includes('svg')) return '.svg'
  return '.webp'
}

function safeName(value) {
  return String(value || 'unknown').replace(/[^A-Za-z0-9_.-]+/g, '_').slice(0, 120)
}

function errorMessage(error) {
  return String(error?.message || error || 'unknown error').slice(0, 500)
}

async function readJson(file, fallback) {
  try {
    return JSON.parse(await readFile(file, 'utf8'))
  } catch (error) {
    if (error?.code === 'ENOENT') return fallback
    throw error
  }
}

function parseArguments(args) {
  const parsed = {
    sources: new Set(),
    skipImages: false,
    headful: false,
    maxDetails: 0,
    detailConcurrency: 4,
    imageConcurrency: 6,
  }
  for (const argument of args) {
    if (argument === '--skip-images') parsed.skipImages = true
    else if (argument === '--headful') parsed.headful = true
    else if (argument.startsWith('--sources=')) {
      argument.slice('--sources='.length).split(',').map((value) => value.trim()).filter(Boolean).forEach((value) => parsed.sources.add(value))
    } else if (argument.startsWith('--max-details=')) {
      parsed.maxDetails = positiveInteger(argument.slice('--max-details='.length), 0)
    } else if (argument.startsWith('--detail-concurrency=')) {
      parsed.detailConcurrency = positiveInteger(argument.slice('--detail-concurrency='.length), 4)
    } else if (argument.startsWith('--image-concurrency=')) {
      parsed.imageConcurrency = positiveInteger(argument.slice('--image-concurrency='.length), 6)
    } else {
      throw new Error(`未知参数：${argument}`)
    }
  }
  return parsed
}

function positiveInteger(value, fallback) {
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : fallback
}
