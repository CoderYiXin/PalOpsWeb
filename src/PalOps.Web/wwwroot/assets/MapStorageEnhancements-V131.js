const SAFE_Z_KEY = 'palops.map.teleport.safeZ'
const HEIGHT_MODE_KEY = 'palops.map.teleport.heightMode'
const WORKSPACE_KEY = 'palops.map.workspace.v7'
const PROGRESS_KEY = 'palops.map.progress.local.v2'
const TOTAL_POIS = 1251
const MAPS = {
  palpagos: {
    bounds: { minimumX: -1922.43790849673, maximumX: 1233.98474945534, minimumY: -2125.2962962963, maximumY: 1031.12636165577 },
    worldToMap: { a: 0, b: 0.00217864923747277, c: -344.226579520697, d: 0.00217864923747277, e: 0, f: 269.908496732026 },
  },
  'world-tree': {
    bounds: { minimumX: -2126.78867102396, maximumX: -1382.13725490196, minimumY: 1026.66775599129, maximumY: 1771.31917211329 },
    worldToMap: { a: 0, b: 0.00217864923747277, c: -344.226579520697, d: 0.00217864923747277, e: 0, f: 269.908496732026 },
  },
}
const TEXT = {
  'zh-CN': {
    point: '定点传送', cancelPoint: '取消选点', player: '玩家传送', pointHint: '选点模式：点击地图任意位置设置传送落点',
    titlePoint: '地图定点传送', titlePlayer: '玩家实时传送', sourcePlayers: '选择在线玩家', sourcePlayer: 'A 玩家（被传送）', targetPlayer: 'B 玩家（实时目标）',
    heightMode: '高度模式', automaticTerrain: '自动匹配地形（推荐）', manualHeight: '手动指定高度', automaticHint: '自动模式不发送 Z，由 PalDefender 根据 X/Y 查找可用地面高度。',
    safeZ: '安全高度 Z', manualHint: '仅在洞穴、地下或特殊区域覆盖自动落地。', execute: '确认传送', cancel: '取消', refresh: '刷新在线玩家', noPlayers: '当前没有可用的在线玩家。',
    risk: '传送属于高危操作。执行前会再次刷新在线玩家。', realtime: 'B 玩家的位置由后端在执行时重新读取。',
    coordinateConfirmAuto: (count, x, y) => `将 ${count} 名玩家传送到地图坐标 X=${x}、Y=${y}，高度由 PalDefender 自动匹配地形，是否继续？`,
    coordinateConfirmManual: (count, x, y, z) => `将 ${count} 名玩家传送到地图坐标 X=${x}、Y=${y}、Z=${z}，是否继续？`,
    playerConfirm: (a, b) => `将玩家 ${a} 传送到玩家 ${b} 的当前实时位置，是否继续？`, success: '玩家传送命令已执行。', failed: '玩家传送失败。', admin: '仅所有者或管理员可以执行玩家传送。',
    progress: '探索进度', discovered: '已发现', collected: '已收集', remaining: '剩余', undiscovered: '未发现',
    repairTitle: '本地存储修复', repairAction: '重新检查并修复',
  },
  'en-US': {
    point: 'Point teleport', cancelPoint: 'Cancel point', player: 'Player teleport', pointHint: 'Targeting mode: click any map point to set the destination',
    titlePoint: 'Map point teleport', titlePlayer: 'Live player teleport', sourcePlayers: 'Online players', sourcePlayer: 'Player A (source)', targetPlayer: 'Player B (live target)',
    heightMode: 'Height mode', automaticTerrain: 'Automatic terrain height (recommended)', manualHeight: 'Manual height', automaticHint: 'Automatic mode omits Z so PalDefender can resolve a usable ground height for X/Y.',
    safeZ: 'Safe Z', manualHint: 'Use only to override automatic landing for caves, underground areas, or special locations.', execute: 'Teleport', cancel: 'Cancel', refresh: 'Refresh online players', noPlayers: 'No online players are available.',
    risk: 'Teleport is a high-risk action. Online players are refreshed again before execution.', realtime: "Player B's position is resolved again by the server when the action runs.",
    coordinateConfirmAuto: (count, x, y) => `Teleport ${count} player(s) to map X=${x}, Y=${y} with terrain height resolved automatically?`,
    coordinateConfirmManual: (count, x, y, z) => `Teleport ${count} player(s) to map X=${x}, Y=${y}, Z=${z}?`,
    playerConfirm: (a, b) => `Teleport ${a} to ${b}'s current live position?`, success: 'Teleport command executed.', failed: 'Teleport failed.', admin: 'Only owners and administrators can teleport players.',
    progress: 'Exploration progress', discovered: 'Discovered', collected: 'Collected', remaining: 'Remaining', undiscovered: 'Undiscovered',
    repairTitle: 'Local storage repair', repairAction: 'Check and repair again',
  },
  'ja-JP': {
    point: '地点転送', cancelPoint: '地点選択を解除', player: 'プレイヤー転送', pointHint: '地点選択モード：マップ上の任意の位置をクリックしてください',
    titlePoint: 'マップ地点転送', titlePlayer: 'プレイヤーリアルタイム転送', sourcePlayers: 'オンラインプレイヤー', sourcePlayer: 'A プレイヤー（転送元）', targetPlayer: 'B プレイヤー（リアルタイム目標）',
    heightMode: '高度モード', automaticTerrain: '地形高度を自動取得（推奨）', manualHeight: '高度を手動指定', automaticHint: '自動モードでは Z を送信せず、PalDefender が X/Y の利用可能な地面高度を検索します。',
    safeZ: '安全高度 Z', manualHint: '洞窟、地下、特殊エリアで自動着地を上書きする場合だけ使用します。', execute: '転送を実行', cancel: 'キャンセル', refresh: 'オンライン更新', noPlayers: '利用可能なオンラインプレイヤーがいません。',
    risk: '転送は高リスク操作です。実行前にオンライン情報を再取得します。', realtime: 'B プレイヤーの位置は実行時にサーバー側で再取得されます。',
    coordinateConfirmAuto: (count, x, y) => `${count} 人をマップ座標 X=${x}、Y=${y} へ転送し、地形高度を自動取得しますか？`,
    coordinateConfirmManual: (count, x, y, z) => `${count} 人をマップ座標 X=${x}、Y=${y}、Z=${z} に転送しますか？`,
    playerConfirm: (a, b) => `${a} を ${b} の現在位置へ転送しますか？`, success: '転送コマンドを実行しました。', failed: '転送に失敗しました。', admin: '所有者または管理者のみ転送できます。',
    progress: '探索進捗', discovered: '発見済み', collected: '収集済み', remaining: '残り', undiscovered: '未発見',
    repairTitle: 'ローカルストレージ修復', repairAction: '再確認して修復',
  },
}
function locale() {
  const value = localStorage.getItem('palops.locale') || 'zh-CN'
  return value.startsWith('en') ? 'en-US' : value.startsWith('ja') ? 'ja-JP' : 'zh-CN'
}
function text() { return TEXT[locale()] }
function safeJson(value, fallback) { try { return JSON.parse(value ?? '') ?? fallback } catch { return fallback } }
function toast(message, type = 'success') {
  const node = document.createElement('div')
  node.className = `po-v131-toast is-${type}`
  node.textContent = message
  document.body.append(node)
  requestAnimationFrame(() => node.classList.add('is-visible'))
  window.setTimeout(() => { node.classList.remove('is-visible'); window.setTimeout(() => node.remove(), 220) }, 3200)
}
let csrfToken = ''
async function request(path, init = {}) {
  const method = (init.method || 'GET').toUpperCase()
  const headers = new Headers(init.headers || {})
  if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
  if (!['GET', 'HEAD', 'OPTIONS'].includes(method)) {
    if (!csrfToken) {
      const response = await fetch('/api/auth/csrf', { credentials: 'same-origin' })
      if (!response.ok) throw new Error(`HTTP ${response.status}`)
      csrfToken = (await response.json()).token || ''
    }
    headers.set('X-CSRF-TOKEN', csrfToken)
  }
  const response = await fetch(path, { ...init, method, headers, credentials: 'same-origin' })
  if (!response.ok) {
    let payload
    try { payload = await response.json() } catch {}
    if (response.status === 401) csrfToken = ''
    throw new Error(payload?.error?.message || payload?.message || `HTTP ${response.status}`)
  }
  if (response.status === 204) return undefined
  return (response.headers.get('content-type') || '').includes('application/json') ? response.json() : response.text()
}
async function isTeleportAuthorized() {
  try {
    const status = await request('/api/auth/status')
    return status?.authenticated && ['Owner', 'Administrator'].includes(status.role)
  } catch { return false }
}
function playerIdentifier(entity) { return entity?.metadata?.userId?.trim() || entity?.metadata?.playerUid?.trim() || '' }
async function loadPlayers() {
  const payload = await request('/api/v1/map/entities?types=player&includeUnresolved=true')
  const items = payload?.data?.items || payload?.items || []
  const unique = new Map()
  for (const entity of items) {
    if (String(entity.type).toLowerCase() !== 'onlineplayer') continue
    const identifier = playerIdentifier(entity)
    if (!identifier) continue
    unique.set(identifier.toLowerCase(), { identifier, label: `${entity.label || identifier} · ${identifier}` })
  }
  return [...unique.values()].sort((a, b) => a.label.localeCompare(b.label))
}
function closeDialog() { document.querySelector('.po-v131-dialog-mask')?.remove() }
function optionHtml(players) { return players.map(player => `<option value="${escapeHtml(player.identifier)}">${escapeHtml(player.label)}</option>`).join('') }
function escapeHtml(value) { const node = document.createElement('span'); node.textContent = String(value); return node.innerHTML }
async function openTeleportDialog(mode, target) {
  const t = text()
  if (!(await isTeleportAuthorized())) { toast(t.admin, 'error'); return }
  closeDialog()
  const mask = document.createElement('div')
  mask.className = 'po-v131-dialog-mask'
  mask.innerHTML = `<section class="po-v131-dialog" role="dialog" aria-modal="true">
    <header><div><small>${mode === 'coordinates' ? t.point : t.player}</small><h3>${mode === 'coordinates' ? t.titlePoint : t.titlePlayer}</h3></div><button type="button" data-close aria-label="${t.cancel}">×</button></header>
    <div class="po-v131-dialog__body"><div class="po-v131-alert">${t.risk}</div><div data-form class="po-v131-loading">${t.refresh}…</div></div>
    <footer><button type="button" data-refresh>${t.refresh}</button><button type="button" data-close>${t.cancel}</button><button type="button" class="is-primary" data-submit disabled>${t.execute}</button></footer>
  </section>`
  document.body.append(mask)
  mask.addEventListener('click', event => { if (event.target === mask || event.target.closest('[data-close]')) closeDialog() })
  const form = mask.querySelector('[data-form]')
  const submit = mask.querySelector('[data-submit]')
  async function renderPlayers() {
    form.className = 'po-v131-form'
    form.innerHTML = `<div class="po-v131-loading">${t.refresh}…</div>`
    submit.disabled = true
    try {
      const players = await loadPlayers()
      if (!players.length) { form.innerHTML = `<div class="po-v131-empty">${t.noPlayers}</div>`; return }
      if (mode === 'coordinates') {
        const storedZValue = localStorage.getItem(SAFE_Z_KEY)
        const storedZ = Number(storedZValue)
        const safeZ = storedZValue === null ? 10000 : Number.isFinite(storedZ) ? storedZ : 10000
        const storedMode = localStorage.getItem(HEIGHT_MODE_KEY)
        const heightMode = storedMode === 'manual' ? 'manual' : 'auto'
        form.innerHTML = `<div class="po-v131-point"><span>Map X=${target.mapX.toFixed(2)}, Y=${target.mapY.toFixed(2)}</span><strong>PalDefender /tp map coordinates</strong></div>
          <label>${t.sourcePlayers}<select data-sources multiple size="7">${optionHtml(players)}</select></label>
          <label>${t.heightMode}<select data-height-mode><option value="auto"${heightMode === 'auto' ? ' selected' : ''}>${t.automaticTerrain}</option><option value="manual"${heightMode === 'manual' ? ' selected' : ''}>${t.manualHeight}</option></select></label>
          <div class="po-v131-info" data-height-hint>${heightMode === 'auto' ? t.automaticHint : t.manualHint}</div>
          <label data-manual-z${heightMode === 'manual' ? '' : ' hidden'}>${t.safeZ}<input data-safe-z type="number" step="100" min="-100000" max="100000" value="${safeZ}"></label>`
        const select = form.querySelector('[data-sources]')
        if (select.options[0]) select.options[0].selected = true
        const modeSelect = form.querySelector('[data-height-mode]')
        const manualZ = form.querySelector('[data-manual-z]')
        const hint = form.querySelector('[data-height-hint]')
        modeSelect.addEventListener('change', () => {
          const manual = modeSelect.value === 'manual'
          manualZ.hidden = !manual
          hint.textContent = manual ? t.manualHint : t.automaticHint
        })
      } else {
        form.innerHTML = `<label>${t.sourcePlayer}<select data-source>${optionHtml(players)}</select></label>
          <label>${t.targetPlayer}<select data-target>${optionHtml(players)}</select></label><div class="po-v131-info">${t.realtime}</div>`
        const source = form.querySelector('[data-source]')
        const targetSelect = form.querySelector('[data-target]')
        if (targetSelect.options.length > 1) targetSelect.selectedIndex = 1
        source.addEventListener('change', () => { if (targetSelect.value === source.value) targetSelect.selectedIndex = [...targetSelect.options].findIndex(item => item.value !== source.value) })
      }
      submit.disabled = false
    } catch (error) {
      form.innerHTML = `<div class="po-v131-empty is-error">${escapeHtml(error instanceof Error ? error.message : t.failed)}</div>`
    }
  }
  mask.querySelector('[data-refresh]').addEventListener('click', renderPlayers)
  submit.addEventListener('click', async () => {
    if (submit.disabled) return
    submit.disabled = true
    try {
      const fresh = await loadPlayers()
      const online = new Set(fresh.map(item => item.identifier.toLowerCase()))
      if (mode === 'coordinates') {
        const select = form.querySelector('[data-sources]')
        const selected = [...select.selectedOptions].map(item => item.value).filter(item => online.has(item.toLowerCase()))
        const heightMode = form.querySelector('[data-height-mode]').value === 'manual' ? 'manual' : 'auto'
        const z = Number(form.querySelector('[data-safe-z]').value)
        if (!selected.length || (heightMode === 'manual' && !Number.isFinite(z))) throw new Error(t.noPlayers)
        const confirmed = heightMode === 'manual'
          ? window.confirm(t.coordinateConfirmManual(selected.length, target.mapX.toFixed(2), target.mapY.toFixed(2), z))
          : window.confirm(t.coordinateConfirmAuto(selected.length, target.mapX.toFixed(2), target.mapY.toFixed(2)))
        if (!confirmed) return
        localStorage.setItem(HEIGHT_MODE_KEY, heightMode)
        if (heightMode === 'manual') localStorage.setItem(SAFE_Z_KEY, String(z))
        const payload = { playerIdentifiers: selected, action: 'teleport-coordinates', x: target.mapX, y: target.mapY, ...(heightMode === 'manual' ? { z } : {}) }
        await request('/api/management/player-action', { method: 'POST', body: JSON.stringify(payload) })
      } else {
        const source = form.querySelector('[data-source]').value
        const destination = form.querySelector('[data-target]').value
        if (!source || !destination || source === destination || !online.has(source.toLowerCase()) || !online.has(destination.toLowerCase())) throw new Error(t.noPlayers)
        if (!window.confirm(t.playerConfirm(source, destination))) return
        await request('/api/management/player-action', { method: 'POST', body: JSON.stringify({ playerIdentifiers: [source], action: 'teleport-player', targetPlayerIdentifier: destination }) })
      }
      closeDialog(); toast(t.success)
    } catch (error) { toast(error instanceof Error ? error.message : t.failed, 'error') }
    finally { if (document.body.contains(submit)) submit.disabled = false }
  })
  await renderPlayers()
}
function normalizedY(latitude) {
  const radians = Math.max(-85.0511287798066, Math.min(85.0511287798066, latitude)) * Math.PI / 180
  return (1 - Math.asinh(Math.tan(radians)) / Math.PI) / 2
}
function yToLatitude(y) { return Math.atan(Math.sinh(Math.PI * (1 - 2 * y))) * 180 / Math.PI }
function mapToVirtual(bounds, mapX, mapY) {
  const nx = (mapX - bounds.minimumX) / (bounds.maximumX - bounds.minimumX)
  const ny = (bounds.maximumY - mapY) / (bounds.maximumY - bounds.minimumY)
  return { lng: -180 + nx * 360, lat: yToLatitude(ny) }
}
function virtualToMap(bounds, lng, lat) {
  const nx = Math.max(0, Math.min(1, (lng + 180) / 360))
  const ny = Math.max(0, Math.min(1, normalizedY(lat)))
  return { mapX: bounds.minimumX + nx * (bounds.maximumX - bounds.minimumX), mapY: bounds.maximumY - ny * (bounds.maximumY - bounds.minimumY) }
}
function resolveClickTarget(canvas, event) {
  const state = safeJson(localStorage.getItem(WORKSPACE_KEY), {})
  const activeMap = state.activeMap === 'world-tree' ? 'world-tree' : 'palpagos'
  const map = MAPS[activeMap]
  const viewport = state.viewports?.[activeMap] || {}
  const center = Array.isArray(viewport.center) ? viewport.center : [(map.bounds.minimumX + map.bounds.maximumX) / 2, (map.bounds.minimumY + map.bounds.maximumY) / 2]
  const zoom = Number.isFinite(viewport.zoom) ? viewport.zoom : 0
  const virtualCenter = mapToVirtual(map.bounds, center[0], center[1])
  const worldSize = 512 * 2 ** zoom
  const centerX = (virtualCenter.lng + 180) / 360 * worldSize
  const centerY = normalizedY(virtualCenter.lat) * worldSize
  const rect = canvas.getBoundingClientRect()
  const x = centerX + (event.clientX - rect.left - rect.width / 2)
  const y = centerY + (event.clientY - rect.top - rect.height / 2)
  const lng = x / worldSize * 360 - 180
  const lat = yToLatitude(y / worldSize)
  const point = virtualToMap(map.bounds, lng, lat)
  return point
}
function setTargeting(workspace, enabled) {
  workspace.dataset.poV131Targeting = enabled ? 'true' : 'false'
  workspace.querySelector('.po-map-canvas')?.classList.toggle('is-coordinate-targeting', enabled)
  const button = workspace.querySelector('[data-po-v131-point]')
  if (button) { button.classList.toggle('is-active', enabled); button.textContent = enabled ? text().cancelPoint : text().point }
  let hint = workspace.querySelector('.po-v131-targeting-hint')
  if (enabled && !hint) { hint = document.createElement('span'); hint.className = 'po-v131-targeting-hint'; workspace.querySelector('.po-map-canvas-status')?.append(hint) }
  if (hint) { hint.textContent = text().pointHint; hint.hidden = !enabled }
}
function enhanceMap(workspace) {
  if (workspace.dataset.poV131Enhanced === 'true') return
  const toolbar = workspace.querySelector('.po-map-toolbar')
  const canvas = workspace.querySelector('.po-map-canvas')
  if (!toolbar || !canvas) return
  workspace.dataset.poV131Enhanced = 'true'
  const point = document.createElement('button')
  point.type = 'button'; point.className = 'el-button po-v131-toolbar-button'; point.dataset.poV131Point = 'true'; point.textContent = text().point
  const player = document.createElement('button')
  player.type = 'button'; player.className = 'el-button po-v131-toolbar-button'; player.dataset.poV131Player = 'true'; player.textContent = text().player
  toolbar.append(point, player)
  point.addEventListener('click', () => setTargeting(workspace, workspace.dataset.poV131Targeting !== 'true'))
  player.addEventListener('click', () => openTeleportDialog('player'))
  canvas.addEventListener('click', event => {
    if (workspace.dataset.poV131Targeting !== 'true' || event.target.closest('.maplibregl-control-container')) return
    event.preventDefault(); event.stopPropagation(); event.stopImmediatePropagation()
    try { const target = resolveClickTarget(canvas, event); setTargeting(workspace, false); openTeleportDialog('coordinates', target) }
    catch (error) { toast(error instanceof Error ? error.message : text().failed, 'error') }
  }, true)
}
function progressCounts() {
  const items = Object.values(safeJson(localStorage.getItem(PROGRESS_KEY), {}))
  return { discovered: items.filter(item => item?.state === 'discovered').length, collected: items.filter(item => item?.state === 'collected').length }
}
function enhanceOverview(overview) {
  let hero = overview.querySelector('.po-map-progress-hero')
  const counts = progressCounts(), completed = Math.min(TOTAL_POIS, counts.discovered + counts.collected), percent = Math.round(completed / TOTAL_POIS * 100), remaining = Math.max(0, TOTAL_POIS - completed), t = text()
  if (!hero) {
    hero = document.createElement('section'); hero.className = 'po-map-progress-hero po-v131-progress-hero'
    overview.querySelector('header')?.after(hero)
  }
  hero.innerHTML = `<div class="po-v131-progress-ring" style="--progress:${percent * 3.6}deg"><strong>${percent}%</strong></div><div><small>${t.progress}</small><h4>${completed} / ${TOTAL_POIS}</h4><span>${t.discovered} ${counts.discovered} · ${t.collected} ${counts.collected}</span></div><div class="po-v131-progress-metric"><b>${remaining}</b><small>${t.remaining}</small></div>`
}
function clickDropdownProgress(index) {
  const trigger = document.querySelector('.po-map-progress-actions .el-dropdown')
  if (!trigger) return
  trigger.click()
  window.setTimeout(() => {
    const menus = [...document.querySelectorAll('.el-popper:not([style*="display: none"]) .el-dropdown-menu__item')]
    menus[index]?.click()
  }, 40)
}
function enhanceProgressActions(actions) {
  if (actions.dataset.poV131Enhanced === 'true') return
  actions.dataset.poV131Enhanced = 'true'
  const workspace = safeJson(localStorage.getItem(WORKSPACE_KEY), {})
  const poiId = String(workspace.selectedKey || '').startsWith('poi:') ? String(workspace.selectedKey).slice(4) : ''
  const current = safeJson(localStorage.getItem(PROGRESS_KEY), {})[poiId]?.state || 'undiscovered'
  const t = text(), states = [['undiscovered', t.undiscovered], ['discovered', t.discovered], ['collected', t.collected]]
  const steps = document.createElement('div'); steps.className = 'po-map-progress-step po-v131-progress-steps'
  states.forEach(([state, label], index) => {
    const button = document.createElement('button'); button.type = 'button'; button.textContent = label; button.className = current === state ? 'is-active' : ''
    button.addEventListener('click', () => clickDropdownProgress(index)); steps.append(button)
  })
  actions.querySelector('.el-button-group')?.classList.add('po-v131-original-progress-controls')
  actions.append(steps)
}
async function enhanceStorage(card) {
  if (card.dataset.poV131StorageChecked === 'true') return
  card.dataset.poV131StorageChecked = 'true'
  card.hidden = true
  try {
    const status = await request('/api/settings/storage/status')
    card.hidden = !status?.repairRequired
    if (status?.repairRequired) {
      const strong = card.querySelector('.card-header strong'); if (strong) strong.textContent = text().repairTitle
      const button = card.querySelector('.storage-init-actions .el-button span'); if (button) button.textContent = text().repairAction
    }
  } catch { card.hidden = true }
}
function scan() {
  document.querySelectorAll('.po-map-workspace').forEach(enhanceMap)
  document.querySelectorAll('.po-map-overview').forEach(enhanceOverview)
  document.querySelectorAll('.po-map-progress-actions').forEach(enhanceProgressActions)
  document.querySelectorAll('#settings-storage').forEach(enhanceStorage)
}
const observer = new MutationObserver(() => { window.clearTimeout(scan.timer); scan.timer = window.setTimeout(scan, 40) })
observer.observe(document.documentElement, { childList: true, subtree: true })
window.addEventListener('storage', scan)
window.setInterval(() => document.querySelectorAll('.po-map-overview').forEach(enhanceOverview), 2500)
scan()
