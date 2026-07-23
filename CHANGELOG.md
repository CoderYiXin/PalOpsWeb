# Changelog

## Unreleased - Platform foundations

- 修复 `PalServerLiveStatusCollector` 中 PalDefender 分支与 REST 聚合分支重复声明 `playerResult` 导致的 C# `CS0136` 编译错误，并加入发布回归门禁。
- 新增统一任务中心：后台操作统一支持队列、优先级、互斥资源、超时、取消、自动重试、人工重试、进度、关联 ID 与持久化历史。
- 将存档解析、维护执行以及 PalServer 启动、停止、重启、强停接入任务中心，移除这些流程中的裸 `Task.Run`。
- 新增统一内存缓存层，支持标签失效、防击穿、命中率与命名空间统计，并接入系统设置、玩家聚合和配置就绪度。
- 扩展配置版本库为多分区脱敏快照，支持自动变更前快照、恢复前自动快照、系统设置差异和最近恢复记录。
- 新增平台健康中心，聚合连接、存档、任务、缓存、后台工作器、日志、磁盘和进程状态，输出健康评分与修复建议。
- 升级系统日志中心，支持级别、类别、时间、异常与全文筛选，提供统计、详情和 CSV/JSONL 导出。
- 新增首次使用配置中心，按必需项和可选项展示配置完成度、缺失项及直接处理入口。
- 统一 API 成功/失败元数据，增加请求追踪响应头，并为长期后台服务加入心跳、故障记录和自动恢复监督。
- 首次未配置时新增能力级静默启动门控：Palworld REST、RCON、PalDefender 本地接口、存档索引、备份、巡检、统计、维护和实时刷新保持暂停；保存对应系统设置后按能力即时激活，无需重启。
- PalOps 与 PalDefender 的 GitHub 远端版本检查不受首次配置门控影响；PalDefender 未配置时只跳过本地 `/version` 读取，版本中心显示“仅远端已检查”，配置完成后自动补读本地版本并进行比较。
- GitHub 版本检查新增双通道容错：优先调用 `api.github.com`，失败后通过 `github.com/<owner>/<repo>/releases/latest` 重定向解析最新标签；版本中心直接显示主通道、备用通道的失败原因或备用通道成功状态。
- 首次安装默认关闭自动化调度；手动连接测试、手动版本检查与系统配置接口仍可使用，并新增首次启动后台静默回归门禁。

## 1.3.1 - 2026-07-23

- Kept online-player markers above guild-base markers and made overlapping players visible and click-prioritized with an expanded hit target.
- Corrected point teleportation to send PalDefender map X/Y coordinates instead of Unreal world coordinates, with server-side coordinate bounds protection.
- Made terrain-aware teleportation the default by omitting Z so PalDefender resolves the destination ground height; retained manual Z as an administrator fallback.
- Added player-to-player teleportation that resolves the destination player's live position at execution time.
- Rebuilt exploration progress with overall completion, category progress, discovered/remaining totals, and unfinished-only filtering.
- Added automatic, idempotent first-start local data initialization and exposed repair actions only after a real initialization failure.
- Reworked page dependency readiness checks to use cached state, shared in-flight requests, and silent background refreshes so configured systems no longer block every navigation.
- Updated package, assembly, file, informational, frontend, documentation, and release metadata to `1.3.1`.
- Rebuilt the Chinese and English GitHub READMEs and recaptured all 37 product views per locale at 1920×1080 using synthetic data, including three distinct resource-grant workflow screenshots.

## 1.3.0 - 2026-07-22

- Rebuilt the Chinese and English GitHub READMEs and recaptured every product module at 1920×1080 with synthetic data, including a dedicated three-step resource-grant walkthrough.
- Added the Diagnostics Center, Incident Center, Player Insights, World Governance, Disaster Recovery, Update Center, Configuration Versions, Operations Playbooks, Security Center, and Integration Center.
- Added shared atomic JSON persistence, role-aware endpoints, CSRF enforcement, structured audit events, high-risk confirmation phrases, and background incident-rule evaluation.
- Added one-time API token disclosure with SHA-256 storage, fixed-time verification, scopes, expiry, revocation, inbound rate limiting, and default-deny external API authorization.
- Connected HTTPS integration subscriptions to the existing webhook queue, retry policy, SSRF validation, and delivery history.
- Updated frontend package, assembly, file, informational, documentation, and release metadata to `1.3.0`.
- Integrated the V1.3.0 Vue production output into `src/PalOps.Web/wwwroot`.
- Fixed all ten V1.3.0 advanced-operation pages opening modal dialogs on initial navigation or reopening immediately after close; Vue refs are now unwrapped through the runtime ref marker rather than own-property detection.
- Prevented reactive objects from rendering as `[object Object]` in filters, notes, review forms, and dialog fields.
- Added a first-deployment readiness endpoint and inline setup guidance for every advanced-operation module, with safe empty-state degradation when REST, RCON, PalDefender, save indexing, backup, or Palworld configuration has not been initialized.
- Extended configuration readiness guidance to every operational page, including configured-but-unavailable service states, automatic 30-second recovery checks, and safe page blocking while required dependencies are missing or still being verified.
- Added a first-use setup checklist and completion progress to System Settings, moved local-data initialization to the first configuration card, and added inline tutorials for storage, world saves, Palworld REST, PalDefender REST, RCON, backups, and automation.

## 1.2.0 - 2026-07-21

- Supplemented the frontend world-map data with nine verified Palworld 1.0 fast-travel records from a pinned PalworldSaveTools revision, including the new Garden, Sakurajima, cape and passage points that were absent from the previous bundle.
- Added a persisted player-position refresh selector with a 3-second default and 1/2/3/5/10/15/30-second options, while keeping guild-base and custom-marker polling on an independent 30-second schedule.
- Offset runtime player icons slightly above their exact coordinate so server markers remain on top without completely hiding a co-located fast-travel POI.
- Reorganized the sidebar into seven role-aware business-domain accordions, keeping all route URLs and permissions unchanged while auto-opening the active group and showing only group icons in collapsed mode.
- Reworked Palworld configuration diagnostics around the values actually accepted by current server configuration files: blank randomizer seeds are valid, integral decimal forms such as `72.000000` are accepted, current-version fields are preserved without one warning per field, and advisory ranges no longer block saving.
- Updated the platform, frontend package, assembly and informational version to `1.2.0`.
- Stabilized the plugin and mod inventory tables by removing sticky action columns, enforcing fixed table layout, containing tab width calculations and isolating horizontal overflow.
- Changed whitelist operations to use the PalDefender `whitelist_add` / `whitelist_remove` RCON commands first, verify the resulting `WhiteList.json`, and fall back to atomic file mutation plus `reloadcfg` only when the command cannot be verified.
- Added strict PalDefender UserId validation so player names and save-game PlayerUID GUIDs cannot be written to the whitelist by mistake.
- Merged PalDefender known players into the discipline identity view immediately, including their latest status, so recently disconnected or kicked accounts remain visible instead of disappearing with the online-player table.
- Added persistent kick records and a dedicated kick-history tab for kicks initiated through the PalOps management action or PalOps RCON console.

### GitHub 首页、双语文档与 CI 修复

- 将 README 顶部技术徽章改为仓库内本地 SVG，并为 .NET、Vue、PalDefender、MapLibre GL JS、Windows Server 与许可证配置明确且可校验的官方点击目标，避免外部徽章代理失效后显示破损图片。
- 将默认中文 `README.md` 升级为 PalDefender 特色项目首页：完整列出 25 个功能模块，并使用当前 1.2.0 Vue 页面生成对应语言的伪数据产品截图。
- `docs/*.md` 默认改为中文，并为架构、构建、部署、地图、Paldeck、PalDefender 和发布检查补充对应 `.en.md` 英文说明。
- 升级 GitHub Actions 到 `actions/checkout@v6`、`actions/setup-dotnet@v5` 和 `actions/setup-node@v6`。
- 删除无运行、构建或发布用途的旧 Python 地图生成器；地图 JSON 作为已审查静态资产直接提交，源码与发布校验均禁止 Python 文件。
- GitHub 发布契约现在持续校验 Action 版本、中英文各 27 张（共 54 张）产品截图、中文默认/英文对应文档，并禁止日文 Markdown 和 Python 源文件。
- 修正发布源码凭据扫描对 Vue `show-password` 属性的误报，同时继续识别独立的密码、Token、Secret 与 API Key 赋值。

### Open-source documentation cleanup

- Removed internal design drafts, implementation plans, one-off defect reports, packaging audits, and historical verification reports from the GitHub source tree.
- Added a curated documentation index and a build-time open-source documentation gate that rejects the removed historical document set, checks current shutdown-security wording, and validates local Markdown links.
- Updated the security policy to match the current `Shutdown 1` plus ten-second verified residual-cleanup flow and the frontend-only map distribution boundary.

### Automatic save indexing, neutral input focus, and self-completing shutdown

- Raised the automatic save-index interval floor and default from 60 seconds to 600 seconds. Existing values below 600 seconds are normalized to 600 seconds when loaded.
- Automatic indexing now creates an initial snapshot when needed, then skips file-triggered parsing while the cached online-player count is explicitly zero. Manual parsing and scheduled parsing remain available.
- Removed the blue Element Plus/browser focus halo from input, textarea, select, date/range, cascader, and input-number controls; focused controls now use only a restrained neutral inset border.
- Replaced the multi-stage normal stop path with one deterministic sequence: verify the current PalServer/Shipping identity, send `Shutdown 1 Server will shut down in 1 seconds`, confirm the RCON response, publish a live 10-to-1 second countdown, then terminate the originally verified PID and clean verified residual `PalServer.exe`/Shipping processes when needed.
- Removed the normal-stop `Save`, `DoExit`, and console-window-close fallbacks that could leave the operation waiting or alternate between a launcher and a restarted Shipping process.
- High-risk RCON commands now require only the administrator acknowledgement checkbox; the obsolete typed `CONFIRM` field and API property were removed.

### Frontend-static world map and complete PalDefender configuration management

- Removed the backend basemap health scan, fixed-map package import/activation pipeline, backend fixed-POI store, backend coordinate projection/calibration services, and obsolete server-side exploration-progress storage.
- Bundled 1,242 localized POIs per locale, 682 raster tiles, map bounds, coordinate transforms, search data, icon metadata, visibility state, and exploration progress in the Vue frontend; future fixed-map updates require only a frontend bundle replacement.
- Kept the backend map API limited to raw server-runtime players, guild bases, and custom markers, with independent parallel source reads so unavailable live-player enrichment does not block the static map.
- Added legacy custom-marker coordinate-space inference before frontend projection and removed stale verification rules that still required deleted map-progress endpoints.
- Added localized field metadata and inline explanations for all 76 documented `Config.json` fields, plus REST configuration/tokens, Pal templates, summon files, import rules, whitelist, and banlist management.
- Added strict JSON/type validation, path traversal and reparse-point protection, a 2 MiB limit, SHA-256 conflict detection, automatic backups, write-through temporary files, atomic replacement, audit records, and `reloadcfg`/restart guidance.
- Added REST API configuration/token generation and management, including secure random token generation and protection against the reserved `TokenExample.json` filename.
- Refined global control focus styling, resource-grant multi-term search and action buttons, and Chinese/English/Japanese notification, history, and PalDefender UI switching.

### World map runtime and save-time fixes

- RCON 常用快捷命令统一移除前导 `/`；旧版粘贴的 `/version` 等命令会在前端与服务端发送前自动规范化为 `version`。

- Starts the `player,guildbase,custom` entity request during Vue setup and deduplicates overlapping realtime refreshes.
- Filters requested runtime entity types before backend snapshot/static processing and reads independent sources concurrently.
- Adds 15 internal World Tree fast-travel points plus the Palpagos approach point.
- Renders dense collectible and Pal icons from zoom 0 while delaying only their labels.
- Displays save-derived timestamps in Beijing time (`Asia/Shanghai`).

## 2026-07-17 - 真实存档玩家、物品、帕鲁领域联结修复

- 使用同一份 Palworld 1.0 世界存档复现“解析完成但玩家等级为 0、背包为空、帕鲁为空”的投影错误。
- 根因是旧投影器把 `Players/*.sav` 当成完整玩家档案；实际昵称、等级、属性、物品槽和帕鲁档案位于 `Level.sav`，玩家存档只保存 UID、位置、最后上线时间以及容器 ID。
- 玩家 UID 现在与 `CharacterSaveParameterMap` 联结，物品容器 ID 与 `ItemContainerSaveData` 联结，队伍/帕鲁终端容器 ID 同时与角色 `SlotId` 和 `CharacterContainerSaveData` 反向索引联结。
- 新增 Palworld 1.0 物品槽 RawData 解码：槽位、数量、物品 ID、创建世界 GUID 和动态物品 GUID。
- 修复 Palworld 1.0 公会 V1 RawData：支持版本标记、会长、成员、等级、据点 ID 和真实公会名称；成员关系仍以角色记录中的 GroupId 交叉校验。
- `PlayerPlatform` 只作为平台枚举诊断，不再误写为 Steam/EOS 账号 ID；真实外部账号 ID 仅由在线 REST/RCON 数据在明确匹配后补充。
- 增加领域联结完整性门槛：世界域非空但所有玩家/物品/帕鲁容器均无法匹配时，保留上一份成功快照，不再发布“完成 100% 但详情全空”的索引。
- 真实存档只读验证得到 8 个玩家档案、255 个玩家物品槽、7,391,630 件物品和 410 个玩家容器帕鲁；世界中另外保留 122 个非玩家容器帕鲁，不会错误归入玩家帕鲁终端。

## 2026-07-17 - 真实存档 GVAS 路径类型修复

- 使用用户提供的 Palworld 1.0 `Level.sav`、`LevelMeta.sav` 和 8 个玩家存档复现 `ReadFString()` 溢出。
- 根因定位到 `MapProperty` 只记录通用 `StructProperty`，旧解析器未按逻辑属性路径推断具体结构类型，把 GUID 键误读为属性包。
- 移植 PalworldSaveTools 当前的 39 条 `PALWORLD_TYPE_HINTS`，覆盖世界、公会、据点、刷怪器、地牢、入侵、油田、补给和玩家本地数据路径。
- `ByteProperty` 数组改为连续 `ReadOnlyMemory<byte>`，避免约千万个字节被装箱为对象并触发投影节点上限。
- `FString` 的负长度计算改为 Int64 边界校验，损坏数据现在返回带逻辑属性路径的 `InvalidDataException`，不再裸抛 `OverflowException`。
- 实际 44,175,125 字节世界 GVAS 已完整走到标准 4 字节尾部；同批 `LevelMeta` 和 8 个玩家 GVAS 也全部完整读取。

## 2026-07-17 - Palworld 1.0 PlM1/ooz 存档兼容

- 修复 `PlM1` 文件头被误判为不受支持的 `PlM`，支持普通与 `CNK+PlM1` 包装。
- 参考 PalworldSaveTools 的 `PlM → OozLib.decompress` 数据流，使用 Wasmtime 在 ASP.NET Core 进程内执行内置 `ooz.wasm`。
- 删除 `Oodle.NET`、`PALOPS_OODLE_LIBRARY` 和游戏目录 DLL 扫描；不再依赖 `oodle-data-shared.dll` 或 `oo2core_9_win64.dll`。
- 保留解压长度、压缩比、内部 GVAS 魔数与只读快照校验。
- 随包提供 `ooz-wasm`/`ooz` 完整对应源代码、原始 npm 包、构建说明和校验和。
- 集成发行版许可证调整为 GPL-3.0-or-later；Wasmtime 44.0.0 仍按 Apache-2.0 WITH LLVM-exception 使用。

## 2026-07-16 - 存档与地图综合修复

- 服务器连接页新增本地数据库与数据目录初始化入口。
- 存档解压支持 raw GVAS、PlZ 0x30/0x31/0x32 和 CNK+PlZ，并增加文件头诊断。
- 显式投影 GroupSaveDataMap 与 BaseCampSaveData，增强公会、成员和据点关联。
- 玩家档案左侧栏和筛选工具栏改为防挤压布局。
- 地图坐标范围按实际标记自动适配，支持可选本地底图。


All notable changes are documented here. The project remains pre-1.0 while save-format compatibility is expanded against current Palworld releases.

## [Unreleased]

### Documentation

- Regenerated the overview, guild base, world map, webhook notification, and resource grant screenshots from the current interface.
- Replaced the single resource grant image with complete step 1, step 2, and step 3 workflow screenshots in both READMEs.

### Added
- Added an Owner-only Palworld configuration center for `PalWorldSettings.ini`, with structured Chinese/English/Japanese field descriptions, raw-text editing, startup-argument management, range/type validation, effective-listener conflict checks, and restart-impact guidance.
- Added server-side preview, SHA-256 optimistic concurrency, automatic pre-change backups, same-directory temporary files, write-through flushes, atomic replacement, audited saves, and save-and-safe-restart workflows.
- Added read-only `WorldOption.sav` detection and explicit warnings that binary world-option editing and partial save writeback are not performed.
- Added a complete PalDefender deployment guide covering DLL placement, first-start directory generation, REST tokens, permissions, Windows firewall boundaries, PalOps integration, upgrades, rollback, and troubleshooting.
- Bundled the fixed world-map configuration, 1,242 multilingual POIs per locale, category statistics, search index, attribution metadata, marker catalog, coordinate resolver, and exploration progress store directly in the Vue application.
- Added frontend architecture gates that reject fixed-map API calls, PBF/vector-source switching, backend coordinate resolution, backend exploration-progress calls, and reintroduction of the map-package upload panel.
- Added a prominent “发送测试” action to the message-notification toolbar for saved channels; Owner and Administrator can send a sample event through the configured provider and receive immediate delivery feedback.
- Windows-local PalServer discovery, metrics, safe lifecycle management, and audited Owner force stop.
- SignalR runtime snapshots and immediate operational events with configurable `5/10/15/30/60/manual` UI refresh modes.
- PalDefender current/latest version comparison and browser-local once-per-release update prompting.
- Encrypted multi-provider Webhook channels, registered-variable templates, retries, alert throttling, and delivery history.

### Fixed
- Expanded plugin inventory discovery to include direct PalDefender installations, UE4SS root files, UE4SS Mods/Plugins directories, and PAK/IoStore mods under `Pal/Content/Paks/~mods` and `LogicMods`; an empty scan now reports every searched location instead of showing an unexplained blank table.
- Kept server runtime map layers, labels, and selection highlights above all fixed POI resources, and refreshed player positions every five seconds while the map page is active.
- Fixed raw-to-structured editor synchronization so a later structured-field edit cannot overwrite changes made in the raw `PalWorldSettings.ini` editor.
- Hardened configuration-path handling against symbolic links/reparse points and made loaded content and SHA-256 originate from the same byte snapshot before optimistic-concurrency checks.
- Fixed normal PalServer shutdown getting stuck after the engine or PalDefender REST stopped: PalOps now confirms `Shutdown 1 Server will shut down in 1 seconds`, shows a live ten-second grace countdown, terminates the originally verified PID, then removes verified launcher/Shipping residue and verifies full exit.
- Fixed the runtime operation dialog permanently displaying the initial `queued/running` POST response. The overview now polls `/api/v1/server-runtime/operations/{operationId}` until `completed` or `failed`, refreshes the runtime snapshot, releases the loading state, and displays the terminal backend error code when applicable.
- Removed the fixed-POI PBF/GeoJSON dual-render race completely. The map now has one local GeoJSON source for fixed POIs and separate runtime GeoJSON sources only for players, guild bases, and custom markers.
- Fixed first-entry map markers being silently dropped while the dynamically added raster source made `map.isStyleLoaded()` temporarily false. Runtime entities and default-selected POIs now load in parallel, remain pending until the application style scaffold is writable, and are flushed immediately without waiting for a later refresh or checkbox toggle.
- Moved the three 1,242-record locale POI arrays out of the map route chunk into cacheable `/map/data/default-pois.<locale>.json` assets, cached generated marker images, lazily created hidden fixed-category layers, and added a localized phased loading overlay tied to engine/runtime/static render milestones.
- Retained `WorldMapPage` with `KeepAlive`, resizing and refreshing MapLibre on activation, while guild-base navigation primes and focuses the known coordinate before the authoritative response.
- Restricted the map backend request to `player`, `guildbase`, and `custom` runtime entities; slow or unavailable server data cannot block an already constructed map or fixed data loaded after the runtime frame.
- Made layer visibility a single synchronous desired-state operation. “全部显示”, “全部隐藏”, group toggles, and individual toggles change MapLibre layout properties without rebuilding sources or waiting for asynchronous tile events.
- Reset persisted map workspace state to schema v5 so stale visibility data from all previous PBF/fallback implementations cannot keep a corrected deployment blank.
- Moved exploration progress to a frontend-local store and coordinate resolution to the bundled affine/bounds resolver, removing two more startup dependencies.
- Moved the advanced raw command console and execution-result panel to the top of the command-control page, ahead of preset commands and history.
- Fixed CMD launch mode so it no longer requires or displays a PalServer.exe setting, uses the Windows `ComSpec` command processor with centralized quoting, derives the installation root from the script/working directory, and accepts the transient root PalServer.exe launcher while waiting for the Shipping process.
- Reworked normal stop to pin the initially verified PID, require a successful one-second RCON shutdown response, publish each remaining grace second, and clean only processes verified against the configured PalServer installation.
- Simplified high-risk RCON confirmation to one explicit acknowledgement checkbox while preserving Owner/Administrator authorization and audit logging.
- Allowed the MapLibre `blob:` worker through the application Content Security Policy.
- Prevented MapLibre from using an exact `-180/180` longitude pair as `maxBounds`, avoiding the antimeridian zero-width/infinite-zoom failure.
- Added the missing `System.Text.Json` namespace import in `PalDefenderVersionService`, resolving the `CS0103` compile errors for `JsonException`.
- Made the runtime overview, top status bar, metric controls, PalServer controls, PalDefender panel, events, and dialogs react immediately to Chinese, English, or Japanese locale changes.
- Read Server FPS, current players, and maximum players from the official Palworld REST `/metrics` endpoint instead of incorrectly looking for those fields in `/info`.
- Changed the default live-status/browser refresh cadence to 10 seconds, reused the cached live snapshot, and skipped player-list aggregation when `/metrics` already contains the current-player count.
- Suppressed successful `System.Net.Http.HttpClient.*` and `Microsoft.Extensions.Http` request lifecycle records from console and System Logs while preserving HTTP warnings/errors and hiding historical routine entries in the log viewer.
- Preferred the real `PalServer-Win64-Shipping[-Cmd].exe` process for resource sampling and retained stable CPU samples between refreshes.
- Confirmed PalDefender version checks use `GET /v1/pdapi/version`; replaced whole-response digit matching with structured PalDefender version parsing so API/schema value `1` is not displayed as the component version.
- Restored structured console startup and failure logging after logging providers were cleared.
- Corrected coordinate-space/layer placement so valid Palpagos coordinates are not rendered outside the map.
- Reconciled teammate and indirectly linked bases instead of discarding bases with incomplete direct fields.
- Kept unresolved coordinate and ownership records available for diagnostics.
- Unified emitted runtime/player notification event names with the selectable Webhook event catalog, including player-kick delivery.
- Restored the login and legacy business-page style contract after the desktop workbench stylesheet split omitted their global layout rules.

### Changed
- Changed the seven business-domain navigation groups to remain expanded by default and removed per-group manual collapse behavior; the compact sidebar mode still hides child links.
- Corrected Palworld port semantics: `PublicPort` is treated as an advertised public endpoint, while the effective game listener is resolved from `-port` or the default `8211`; disabled RCON/REST endpoints no longer participate in listener-conflict checks.
- Extended structured startup arguments with the documented performance switches and worker-thread parameter while preserving unknown command-line options in raw form.
- Removed the map-package management panel from System Settings because the production map page no longer consumes fixed-map packages.
- Rebuilt the guild workspace with a compact selectable guild list, summary strip, improved member identity/status rows, and separate resolved/pending base cards containing coordinate source, association reason, worker/object counts, and map actions.
- Replaced temporary text-in-shape map markers with a shared 34-icon game-map catalog used by both MapLibre and the left filter tree, with category palettes, silhouettes, outlined badges, zoom-aware sizing, and high-contrast labels.
- Renamed navigation entries to compact four-character Chinese labels and added missing menu icons.
- Reorganized guild, member, base, runtime, notification, and map workflows for desktop use.
- Replaced Leaflet and the independent immersive shell with one persistent MapLibre instance inside `WorkbenchShell`. Fixed map content is frontend-local; only server runtime entities remain remote.
- Expanded multilingual documentation, deployment guidance, release verification, and screenshot coverage.

### Security
- Encrypted Webhook secrets with ASP.NET Core Data Protection and masked them in API responses.
- Added destination validation and size/redirect limits for outbound Webhooks.
- Restricted force stop and launch-configuration mutation to Owner with audit records.
- Added source/release verification for secrets, runtime data, documentation links, and required notices.

### Known limitations
- `WorldOption.sav` is detected and reported but remains read-only; this phase intentionally avoids binary save mutation, per-player restoration, and partial world-save writeback.
- PalServer process control is supported only on the same Windows host as PalOps Web.
- Server FPS and maximum-player capacity require the configured Palworld REST API to expose and authorize `/metrics`; the UI reports partial unavailability when that request fails.
- Offline release packaging requires complete Palpagos and World Tree tile sets.
