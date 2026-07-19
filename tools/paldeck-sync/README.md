# Paldeck 六类离线同步工具

维护期从以下 Paldeck 页面抓取 **内部 ID、英文名称、分类、详情页地址和静态图片**，并生成带中文名称的离线 JSON：

- `https://paldeck.cc/pals`
- `https://paldeck.cc/items`
- `https://paldeck.cc/technology`
- `https://paldeck.cc/buildings`
- `https://paldeck.cc/passives`
- `https://paldeck.cc/skills`

PalOps Web 运行时不会访问 Paldeck。同步结果位于 `tools/paldeck-sync/output`，图片位于 `output/images/<类型>`。

## 运行

```powershell
cd tools\paldeck-sync
npm install
npx playwright install chromium
npm run sync
```

常用模式：

```powershell
# 只抓 JSON，不下载图片
npm run sync:data-only

# 只更新物品和帕鲁
npm run sync:items-pals

# 调试前 20 个详情页并显示浏览器
node sync.mjs --sources=pals,items,buildings --max-details=20 --headful
```

支持参数：

| 参数 | 说明 |
|---|---|
| `--sources=pals,items` | 限定数据源 |
| `--skip-images` | 不下载静态图片 |
| `--headful` | 显示 Chromium，便于排查页面结构变化 |
| `--max-details=20` | 每类最多访问多少个详情页；`0` 表示全部 |
| `--detail-concurrency=4` | 详情页并发数 |
| `--image-concurrency=6` | 图片下载并发数 |

## ID 提取规则

- 帕鲁：进入详情页，读取名称下方的内部 ID，例如 `SheepBall`。
- 物品：进入详情页，读取 `Asset Name`，例如 `Money`。
- 建筑：读取标题括号中的 BuildingID，并同时保存 `Blueprint Class`。
- 科技、被动、技能：从列表卡片的 `data-id`、`code` 或内部标识文本中提取。
- 未解析到 ID 的记录会保留在 JSON，并列入 `validation.missingIds`，不会自行伪造 ID。

## 中文名称来源

每条记录都有 `translationStatus`：

1. `manual-override`：来自 `translations.zh-CN.json` 的人工覆盖。
2. `local-catalog-id`：按内部 ID 命中 PalOps Web 已审核的物品/帕鲁目录。
3. `local-catalog-name`：按唯一英文名命中本地目录。
4. `glossary-assisted`：按内置术语表组合翻译，必须人工抽查。
5. `untranslated`：没有可靠译名，保留英文，避免伪造中文。

人工修正请写入 `translations.zh-CN.json`，键可以是 `类型:内部ID` 或 `类型:英文名`。

## 上线前检查

1. 查看每类 JSON 中的 `validation`。
2. 确保 `missingIds`、`duplicateIds` 和 `imageErrors` 已人工处理。
3. 对 `glossary-assisted` 和 `untranslated` 逐条抽查。
4. 仅将审核通过的物品/帕鲁结果合并到 `src/PalOps.Web/Seed`。
5. 运行 `dotnet run --project tools/PalOps.Tooling -c Release -- catalog normalize --root .`，重新生成目录审计报告。

Paldeck 是非官方社区项目，与 Pocketpair 无关联；页面结构变化时，应先用 `--max-details` 小批量验证抓取器。
