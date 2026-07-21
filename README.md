<div align="center">

# PalOps Web

### 基于 PalDefender 防作弊插件能力打造的 Palworld 一体化服务器运营控制台

**1.2.0** 将 PalServer 生命周期、实时监控、离线世界地图、玩家与公会、存档解析、配置维护、玩家纪律、插件模组、通知与权限审计整合到一个现代化 Web 工作台。

[English](README.en.md) · [功能参考](docs/features.md) · [中文文档](docs/README.md) · [English Docs](docs/README.en.md)

<a href="https://dotnet.microsoft.com/"><img src="docs/images/badges/dotnet-10.svg" alt=".NET 10" height="28"></a>
<a href="https://vuejs.org/"><img src="docs/images/badges/vue-3.svg" alt="Vue 3" height="28"></a>
<a href="https://github.com/Ultimeit/PalDefender"><img src="docs/images/badges/paldefender-integrated.svg" alt="PalDefender" height="28"></a>
<a href="https://maplibre.org/maplibre-gl-js/docs/"><img src="docs/images/badges/maplibre-offline-map.svg" alt="MapLibre GL JS" height="28"></a>
<a href="https://learn.microsoft.com/zh-cn/windows-server/"><img src="docs/images/badges/windows-server.svg" alt="Windows Server" height="28"></a>
<a href="LICENSE"><img src="docs/images/badges/license-gpl3.svg" alt="GPL-3.0-or-later" height="28"></a>

</div>

![PalOps Web 1.2.0 运行概览](docs/images/zh-CN/overview-dashboard.webp)

> README 中的界面图全部来自 1.2.0 当前 Vue 前端，使用专用伪数据与保留地址生成；中文 README 只引用中文界面截图，英文 README 只引用英文界面截图。

## 1.2.0 重点更新

- **世界地图实时层**：玩家标记始终位于固定 POI 上层；玩家位置刷新间隔支持 1/2/3/5/10/15/30 秒，默认 3 秒；公会据点和自定义标记保持独立 30 秒刷新。
- **快速传送数据补全**：前端离线包新增 9 个已验证 Palworld 1.0 快速传送记录，内置 94 个快速传送点并显示与当前已知 149 条上游记录的覆盖差距。
- **七级业务菜单**：侧边栏按服务器态势、运营、配置维护、玩家安全、存档数据、扩展和系统治理组织，当前分组自动展开。
- **Palworld 配置中心**：结构化管理 `PalWorldSettings.ini` 与启动参数，支持语法/范围/冲突校验、差异预览、修改前备份、原子替换和安全重启。
- **诊断与持续运维**：新增服务器历史统计、维护中心、崩溃守护、存档差异、玩家纪律及插件与模组管理。
- **PalDefender 管理增强**：白名单优先使用 `whitelist_add` / `whitelist_remove` RCON，并验证 `WhiteList.json`；新增 UserId 严格校验、踢出记录和离线身份保留。

## 为什么选择 PalOps Web

PalOps Web 不是通用云面板。它面向**与 PalServer 同机部署的 Windows 专用服务器**，用于统一解决进程控制、玩家数据、地图、存档、PalDefender、防护配置、通知和审计分散的问题。

- **一体化**：一个界面覆盖监控、运营、配置、维护、安全、存档、扩展和治理。
- **本地优先**：存档只读解析、地图瓦片/POI、本地目录和审计数据均可离线运行。
- **安全写入**：身份、角色权限、CSRF、SHA-256 并发检查、修改前备份、原子替换和结构化审计。
- **PalDefender 深度集成**：REST、扩展 RCON、白名单、封禁、版本与配置文件管理。
- **开源发布可验证**：双语 README、双语产品截图、文档契约和 GitHub Actions 持续校验。

> PalDefender 与 PalOps Web 是独立项目。未安装 PalDefender 时，基础进程控制、存档解析、地图、备份和部分原生 REST/RCON 能力仍可使用；依赖 PalDefender 的实时、防护和扩展操作会明确显示为未配置。

## 完整功能模块

| 分组 | 模块 | 主要能力 |
|---|---|---|
| 服务器态势 | **运行概览** | PalServer 进程、主机资源、在线人数、Server FPS、版本中心、存档与实时事件 |
| 服务器态势 | **服务器统计** | 在线人数、FPS、CPU、内存、异常、备份与 Webhook 的小时/日趋势和保留状态 |
| 服务器数据 | **玩家管理** | 在线与存档玩家、等级、公会、坐标、背包、帕鲁、身份来源和最后在线 |
| 服务器数据 | **公会据点** | 公会成员、会长、据点归属证据、坐标、工作帕鲁和地图跳转 |
| 服务器数据 | **世界地图** | Palpagos / World Tree 离线地图、1,251 条 POI、实时玩家、据点、自定义标记、快速传送和 1/2/3/5/10/15/30 秒玩家刷新 |
| 服务器运营 | **物资发放** | 多玩家选择、中文/英文/内部 ID 多元素检索、物品与帕鲁清单、经验和科技点发放 |
| 服务器运营 | **消息发送** | 全服广播、警告、定向消息、在线玩家选择、踢出和 PalDefender 重载 |
| 服务器运营 | **RCON 控制台** | 标准与 PalDefender 扩展命令、能力探测、高危确认、输出和历史 |
| 配置与运维 | **Palworld 配置中心** | 结构化编辑 PalWorldSettings.ini 与启动参数，校验、差异、备份和安全重启 |
| 配置与运维 | **自动任务** | Cron/周期任务、自动备份、公告、保存、重启、风险等级和执行历史 |
| 配置与运维 | **维护中心与崩溃守护** | 维护公告、保存、备份、停服、脚本、启动、健康验证、自动恢复和熔断 |
| 数据与存档 | **物品与帕鲁目录** | 离线目录、图标、分类、别名、收藏、导入和搜索 |
| 玩家安全 | **玩家纪律** | 白名单、封禁、在线玩家、身份关联、违规、踢出记录和操作审计 |
| 玩家安全 | **PalDefender 防护组件** | 版本、连接、配置文件生成、字段说明、校验、备份、原子保存和 reloadcfg 提示 |
| 权限治理 | **权限管理** | Owner、Administrator、Operator、Auditor、Viewer 多角色账户与启停 |
| 权限治理 | **审计日志** | 登录、配置、进程、RCON、存档、玩家纪律和权限操作的结构化审计 |
| 数据与存档 | **存档备份** | 手动/自动备份、SHA-256、保留策略、下载、恢复预检和受保护恢复 |
| 数据与存档 | **存档差异** | 只读比较两次解析快照，跟踪玩家、公会、据点、物品、帕鲁变化与异常 |
| 数据与存档 | **存档解析** | Level.sav / Players 只读快照、自动解析、格式检测、进度和失败回退 |
| 扩展管理 | **插件与模组** | PalDefender、UE4SS、服务器模组和脚本的版本、哈希、依赖、兼容性、备份与回滚 |
| 消息中心 | **消息通知** | 企业微信、钉钉、飞书、Discord、Slack、Telegram 和通用 JSON Webhook |
| 消息中心 | **推送记录** | 投递成功、重试、失败、HTTP 状态、耗时、请求/响应摘要和错误原因 |
| 系统管理 | **系统日志** | 业务日志、级别筛选、全文搜索、异常详情和分页 |
| 系统管理 | **系统设置** | Palworld REST、PalDefender REST、RCON、存档、备份、自动任务与安全选项 |
| 系统管理 | **关于系统** | 版本、依赖、地图数据来源、许可证、项目边界和发布信息 |

更细的行为、权限与数据来源见 [完整功能参考](docs/features.md)。

## 产品界面

### 运行态势与历史统计

<table>
<tr>
<td width="50%"><img src="docs/images/zh-CN/overview-dashboard.webp" alt="运行概览"><br><sub><b>运行概览</b>：PalServer、主机指标、版本、存档与实时事件集中监控</sub></td>
<td width="50%"><img src="docs/images/zh-CN/server-statistics.webp" alt="服务器统计"><br><sub><b>服务器统计</b>：玩家、FPS、资源和运营事件趋势</sub></td>
</tr>
</table>

### 玩家、公会与世界地图

<table>
<tr>
<td width="50%"><img src="docs/images/zh-CN/player-management.webp" alt="玩家管理"><br><sub><b>玩家管理</b>：在线/离线玩家、角色属性、背包与帕鲁档案</sub></td>
<td width="50%"><img src="docs/images/zh-CN/guild-bases.webp" alt="公会据点"><br><sub><b>公会据点</b>：成员、归属证据、据点详情与地图定位</sub></td>
</tr>
</table>

<table>
<tr>
<td ><img src="docs/images/zh-CN/world-map.webp" alt="世界地图"><br><sub><b>世界地图</b>：离线底图、快速传送、实时玩家、据点与自定义标记</sub></td>
</tr>
</table>

### 物资发放三步流程

<table>
<tr>
<td ><img src="docs/images/zh-CN/resource-grant-step-1.webp" alt="物资发放 · 选择玩家"><br><sub><b>物资发放 · 选择玩家</b>：按玩家、公会和在线状态选择多个目标</sub></td>
</tr>
</table>

<table>
<tr>
<td ><img src="docs/images/zh-CN/resource-grant-step-2.webp" alt="物资发放 · 选择资源"><br><sub><b>物资发放 · 选择资源</b>：分类、多元素搜索、物品/帕鲁选择与批量加入</sub></td>
</tr>
</table>

<table>
<tr>
<td ><img src="docs/images/zh-CN/resource-grant-step-3.webp" alt="物资发放 · 核对清单"><br><sub><b>物资发放 · 核对清单</b>：确认目标、资源、数量和最终执行范围</sub></td>
</tr>
</table>

### 日常运营、配置与维护

<table>
<tr>
<td width="50%"><img src="docs/images/zh-CN/message-center.webp" alt="消息发送"><br><sub><b>消息发送</b>：公告、警告、定向消息和玩家操作</sub></td>
<td width="50%"><img src="docs/images/zh-CN/rcon-console.webp" alt="RCON 控制台"><br><sub><b>RCON 控制台</b>：命令模板、风险确认、能力探测与返回历史</sub></td>
</tr>
</table>

<table>
<tr>
<td width="50%"><img src="docs/images/zh-CN/palworld-configuration.webp" alt="Palworld 配置中心"><br><sub><b>Palworld 配置中心</b>：结构化参数、启动参数、校验与安全保存</sub></td>
<td width="50%"><img src="docs/images/zh-CN/automation-jobs.webp" alt="自动任务"><br><sub><b>自动任务</b>：周期任务、风险等级、下次运行和执行历史</sub></td>
</tr>
</table>

<table>
<tr>
<td width="50%"><img src="docs/images/zh-CN/maintenance-center.webp" alt="维护中心与崩溃守护"><br><sub><b>维护中心与崩溃守护</b>：维护编排、自动恢复、健康验证和熔断状态</sub></td>
<td width="50%"><img src="docs/images/zh-CN/catalog-management.webp" alt="物品与帕鲁目录"><br><sub><b>物品与帕鲁目录</b>：离线目录、图标、分类、别名、收藏和导入</sub></td>
</tr>
</table>

### 玩家安全、PalDefender 与扩展

<table>
<tr>
<td width="50%"><img src="docs/images/zh-CN/player-discipline.webp" alt="玩家纪律"><br><sub><b>玩家纪律</b>：白名单、封禁、身份、违规、踢出与审计</sub></td>
<td width="50%"><img src="docs/images/zh-CN/paldefender-console.webp" alt="PalDefender 防护组件"><br><sub><b>PalDefender 防护组件</b>：连接、版本、配置文件、字段说明与原子保存</sub></td>
</tr>
</table>

<table>
<tr>
<td width="50%"><img src="docs/images/zh-CN/plugin-management.webp" alt="插件与模组"><br><sub><b>插件与模组</b>：版本、依赖、兼容性、升级、备份和回滚</sub></td>
<td width="50%"><img src="docs/images/zh-CN/user-management.webp" alt="权限管理"><br><sub><b>权限管理</b>：多角色账户、启停状态和最近登录</sub></td>
</tr>
</table>

### 存档与变化追踪

<table>
<tr>
<td width="50%"><img src="docs/images/zh-CN/save-backups.webp" alt="存档备份"><br><sub><b>存档备份</b>：备份统计、校验、下载、恢复和删除</sub></td>
<td width="50%"><img src="docs/images/zh-CN/save-diff.webp" alt="存档差异"><br><sub><b>存档差异</b>：快照比较、变化分类和异常提示</sub></td>
</tr>
</table>

<table>
<tr>
<td width="50%"><img src="docs/images/zh-CN/save-index.webp" alt="存档解析"><br><sub><b>存档解析</b>：快照状态、自动解析、格式检测和手动任务</sub></td>
<td width="50%"><img src="docs/images/zh-CN/audit-log.webp" alt="审计日志"><br><sub><b>审计日志</b>：关键操作、结果、来源地址和结构化详情</sub></td>
</tr>
</table>

### 通知与系统治理

<table>
<tr>
<td width="50%"><img src="docs/images/zh-CN/notification-channels.webp" alt="消息通知"><br><sub><b>消息通知</b>：多渠道 Webhook、事件订阅、模板与重试</sub></td>
<td width="50%"><img src="docs/images/zh-CN/notification-history.webp" alt="推送记录"><br><sub><b>推送记录</b>：投递状态、HTTP 结果、耗时和失败原因</sub></td>
</tr>
</table>

<table>
<tr>
<td width="50%"><img src="docs/images/zh-CN/system-logs.webp" alt="系统日志"><br><sub><b>系统日志</b>：业务日志、级别筛选和异常定位</sub></td>
<td width="50%"><img src="docs/images/zh-CN/system-settings.webp" alt="系统设置"><br><sub><b>系统设置</b>：连接、存档、备份、自动任务和安全配置</sub></td>
</tr>
</table>

<table>
<tr>
<td ><img src="docs/images/zh-CN/about-project.webp" alt="关于系统"><br><sub><b>关于系统</b>：版本、参考项目、数据来源和开源声明</sub></td>
</tr>
</table>

## PalDefender 特色集成

```mermaid
flowchart LR
    Admin[管理员] --> PalOps[PalOps Web]
    PalOps -->|REST Bearer Token| PDREST[PalDefender REST]
    PalOps -->|标准与扩展命令| RCON[PalDefender RCON]
    PalOps -->|校验 / 备份 / 原子替换| Config[PalDefender JSON 配置]
    PDREST --> Runtime[玩家 / 指标 / 版本 / 防护数据]
    RCON --> Operations[白名单 / 封禁 / 广播 / reloadcfg]
    Config --> Protection[反作弊 / 日志 / 管理员 / 游戏规则]
```

PalOps 支持 PalDefender 版本比较、REST Token 连通性、配置文件生成、76 个已知字段的本地化说明、JSON/type/path 校验、SHA-256 冲突检测、自动备份、原子保存、白名单/封禁、扩展 RCON 能力探测和重载/重启提示。

部署与 Token 配置见 [PalDefender 部署](docs/paldefender-deployment.md)；配置边界见 [PalDefender 配置管理](docs/paldefender-configuration-management.md)。

## 系统架构

```mermaid
flowchart LR
    Browser[浏览器
Vue 3 + Element Plus + MapLibre] -->|Cookie + CSRF| Web[PalOps.Web
ASP.NET Core .NET 10]
    Web -->|SignalR| Browser
    Web --> Runtime[PalServer / Shipping
Windows 进程控制]
    Web --> Save[Level.sav / Players
只读快照解析]
    Web --> PalREST[Palworld REST]
    Web --> RCON[Palworld / PalDefender RCON]
    Web --> Defender[PalDefender REST 与配置目录]
    Web --> Notify[Webhook 渠道]
    Web --> Data[(本地 JSON / JSONL
设置、备份、审计、统计)]
    Browser --> Map[前端离线地图
瓦片、POI、探索状态]
```

关键安全边界：

- 浏览器不直接持有 Palworld、RCON 或 PalDefender 凭据；
- 所有写操作经过登录、角色权限、CSRF 和审计检查；
- 生命周期操作重新验证 PID、可执行文件和安装目录；
- 存档解析只读取私有快照，失败时保留最后一份成功索引；
- PalDefender 与 Palworld 配置只允许受控路径，并采用备份、临时文件、写穿和原子替换；
- 固定地图数据完全由前端静态资源提供，后端只提供服务器实时玩家、据点和自定义标记。

## 快速开始

### 环境要求

- Windows 10/11 或 Windows Server；
- .NET 10 SDK；
- Node.js 22；
- 本机 Palworld Dedicated Server；
- 推荐安装 PalDefender，以启用完整实时、防护和扩展管理能力。

### 获取源码并构建

```powershell
git clone https://github.com/CoderYiXin/PalOpsWeb.git
cd PalOpsWeb
.\scripts\build.ps1
```

### 生成 Windows x64 发布包

```powershell
.\scripts\fetch-map-tiles.ps1 -Layer all
.\scripts\publish-win-x64.ps1 -Version 1.2.0
```

首次启动后完成 Owner 初始化，依次配置 Palworld REST、RCON、PalDefender REST、存档和备份目录，再验证玩家、地图、配置、维护和通知模块。仅通过受信任 LAN、VPN 或正确配置的 HTTPS 反向代理开放管理入口。

完整步骤见 [构建说明](docs/build.md) 与 [部署说明](docs/deployment.md)。

## 文档

| 主题 | 中文 | English |
|---|---|---|
| 文档首页 | [docs/README.md](docs/README.md) | [docs/README.en.md](docs/README.en.md) |
| 完整功能参考 | [features.md](docs/features.md) | [features.en.md](docs/features.en.md) |
| 架构 | [architecture.md](docs/architecture.md) | [architecture.en.md](docs/architecture.en.md) |
| 构建 | [build.md](docs/build.md) | [build.en.md](docs/build.en.md) |
| 部署 | [deployment.md](docs/deployment.md) | [deployment.en.md](docs/deployment.en.md) |
| PalDefender 部署 | [paldefender-deployment.md](docs/paldefender-deployment.md) | [paldefender-deployment.en.md](docs/paldefender-deployment.en.md) |
| PalDefender 配置 | [paldefender-configuration-management.md](docs/paldefender-configuration-management.md) | [paldefender-configuration-management.en.md](docs/paldefender-configuration-management.en.md) |
| 地图数据 1.2.0 | [world-map-data-1.2.0.md](docs/world-map-data-1.2.0.md) | [world-map-data-1.2.0.en.md](docs/world-map-data-1.2.0.en.md) |
| 截图规范 | [images/README.md](docs/images/README.md) | [images/README.en.md](docs/images/README.en.md) |
| 发布检查 | [release-checklist.md](docs/release-checklist.md) | [release-checklist.en.md](docs/release-checklist.en.md) |

## GitHub Actions

`.github/workflows/build.yml` 在 push、Pull Request 和手动触发时执行前端契约、TypeScript、Vite、npm 高危依赖审计、.NET 10 restore/build、目录/地图/文档/源码校验，以及运行数据、密钥、Python 源文件、日文 Markdown 和编译缓存残留检查。

## 安全、贡献与许可证

不要把 PalOps、Palworld REST、PalDefender REST 或 RCON 直接暴露到公网；不要提交 `data`、存档、日志、数据库、Data Protection 密钥、密码、Token 或 Cookie。

漏洞报告见 [SECURITY.md](SECURITY.md)，贡献流程见 [CONTRIBUTING.md](CONTRIBUTING.md)，第三方资源见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

PalOps Web 以 **GNU GPL v3 或更高版本**发布。Palworld 及相关名称、商标和游戏资产归各自权利人所有；本项目与 Pocketpair 无隶属或背书关系。
