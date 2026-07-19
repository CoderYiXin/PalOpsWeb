<div align="center">

# PalOps Web

### 基于 PalDefender 防作弊插件能力打造的 Palworld 一体化服务器运营控制台

PalOps Web 将 **PalDefender REST / RCON / 配置文件管理** 与 PalServer 生命周期、存档解析、玩家与公会、离线世界地图、物资发放、备份恢复、自动任务、通知推送、权限审计整合到一个现代化 Web 工作台中。

[English](README.en.md) · [中文文档](docs/README.md) · [English Docs](docs/README.en.md)

<a href="https://dotnet.microsoft.com/"><img src="docs/images/badges/dotnet-10.svg" alt=".NET 10" height="28"></a>
<a href="https://vuejs.org/"><img src="docs/images/badges/vue-3.svg" alt="Vue 3" height="28"></a>
<a href="https://github.com/Ultimeit/PalDefender"><img src="docs/images/badges/paldefender-integrated.svg" alt="PalDefender" height="28"></a>
<a href="https://maplibre.org/maplibre-gl-js/docs/"><img src="docs/images/badges/maplibre-offline-map.svg" alt="MapLibre GL JS" height="28"></a>
<a href="https://learn.microsoft.com/zh-cn/windows-server/"><img src="docs/images/badges/windows-server.svg" alt="Windows Server" height="28"></a>
<a href="LICENSE"><img src="docs/images/badges/license-gpl3.svg" alt="GPL-3.0-or-later" height="28"></a>

</div>

![PalOps Web 运行概览](docs/images/overview-dashboard.webp)

## 为什么选择 PalOps Web

PalOps Web 不是通用云面板。它面向**与 PalServer 同机部署的 Windows 专用服务器**，重点解决社区服长期运营中最常见的割裂问题：进程控制在命令行、玩家数据在存档工具、据点坐标在第三方地图、封禁与防作弊配置在 PalDefender 文件、通知和审计又分散在其他系统。

项目以 PalDefender 防作弊插件提供的 REST、RCON 与配置能力作为特色功能底座，同时保留 Palworld 原生 REST、RCON 和本地存档解析链路。管理员可以在一个界面内完成：

- 查看 PalServer、主机资源、在线玩家与 Server FPS；
- 管理 PalDefender 版本、REST 连接、白名单、封禁和完整配置文件；
- 从存档解析玩家、公会、据点、物品与帕鲁，并直接跳转到离线地图；
- 批量发放物品、帕鲁、经验和科技点；
- 执行安全停服、自动任务、备份恢复、通知推送和权限审计；
- 在不依赖外部地图服务的情况下浏览 Palpagos / World Tree。

> PalDefender 是本项目的重要特色集成，但 PalOps Web 与 PalDefender 仍是独立程序。未安装 PalDefender 时，基础进程控制、存档解析、地图、备份等功能仍可按配置使用；依赖 PalDefender 的防护、扩展 RCON 和实时数据能力会明确显示为未配置。

## 完整功能模块

| 分组 | 模块 | 主要能力 |
|---|---|---|
| 服务器 | **运行概览** | PalServer 状态、PID、运行时长、CPU/内存、磁盘、在线人数、Server FPS、事件流 |
| 服务器 | **玩家管理** | 在线/离线玩家、等级、公会、坐标、背包、帕鲁、身份来源与最后在线时间 |
| 服务器 | **公会据点** | 公会成员、会长、已定位与待确认据点、据点归属证据、地图跳转 |
| 服务器 | **世界地图** | Palpagos / World Tree 离线瓦片、1,242 条 POI、服务器玩家、据点、自定义标记、探索进度 |
| 运营 | **物资发放** | 多玩家选择、空格多元素搜索、物品/帕鲁购物车、经验与科技点、任务结果 |
| 运营 | **消息发送** | 全服公告、警告、私聊、玩家选择、PalDefender 重载与管理动作 |
| 运营 | **RCON 指令控制** | 标准 RCON、PalDefender 扩展指令、能力探测、高危操作复选确认、响应历史 |
| 运营 | **自动任务** | Cron/周期任务、自动备份、公告、重启、风险等级、执行历史 |
| 存档 | **存档备份** | 手动与自动备份、SHA-256 校验、保留策略、下载、恢复预检与受保护恢复 |
| 通知 | **消息通知** | 企业微信、钉钉、飞书、Discord、Slack、Telegram、通用 JSON Webhook |
| 通知 | **推送记录** | 成功、重试与失败记录，HTTP 状态、耗时、请求/响应摘要和错误原因 |
| 系统 | **系统设置** | Palworld REST、PalDefender REST、RCON、存档目录、备份与自动任务设置 |
| 系统 | **防护组件** | PalDefender 版本、REST 状态、配置文件生成、中文字段说明、校验、备份与原子保存 |
| 系统 | **存档解析** | Level.sav / Players 只读快照、解析进度、自动解析策略、格式检测和失败回退 |
| 系统 | **目录管理** | 物品和帕鲁目录搜索、分类、别名、收藏和导入管理 |
| 系统 | **审计日志** | 登录、配置、进程、备份、RCON、通知和权限操作的结构化审计 |
| 系统 | **系统日志** | 降噪后的业务日志、级别筛选、全文搜索和异常详情 |
| 系统 | **权限管理** | Owner、Administrator、Operator、Auditor、Viewer 分级账户与启停控制 |
| 系统 | **关于系统** | 版本、开源依赖、地图数据来源、许可证和项目边界 |

## 产品界面

以下图片均来自当前前端构建，并使用脱敏演示数据生成。

### 服务器态势与数据管理

<table>
<tr>
<td width="50%"><img src="docs/images/overview-dashboard.webp" alt="运行概览"><br><sub><b>运行概览</b>：服务器、主机指标、存档、PalDefender 和事件集中监控</sub></td>
<td width="50%"><img src="docs/images/player-management.webp" alt="玩家管理"><br><sub><b>玩家管理</b>：在线状态、角色属性、背包与帕鲁档案</sub></td>
</tr>
<tr>
<td width="50%"><img src="docs/images/guild-bases.webp" alt="公会据点"><br><sub><b>公会据点</b>：成员、归属证据、据点详情和地图定位</sub></td>
<td width="50%"><img src="docs/images/world-map.webp" alt="世界地图"><br><sub><b>世界地图</b>：离线底图、固定 POI、玩家和服务器据点</sub></td>
</tr>
</table>

### 日常运营工具

<table>
<tr>
<td width="50%"><img src="docs/images/resource-grant.webp" alt="物资发放"><br><sub><b>物资发放</b>：多目标选择、资源搜索、快捷操作和发放任务</sub></td>
<td width="50%"><img src="docs/images/message-center.webp" alt="消息发送"><br><sub><b>消息发送</b>：公告、警告、私聊和玩家操作</sub></td>
</tr>
<tr>
<td width="50%"><img src="docs/images/rcon-console.webp" alt="RCON 指令控制"><br><sub><b>RCON 指令控制</b>：标准与 PalDefender 指令、风险识别和响应控制台</sub></td>
<td width="50%"><img src="docs/images/automation-jobs.webp" alt="自动任务"><br><sub><b>自动任务</b>：周期任务、风险等级、下次运行和历史结果</sub></td>
</tr>
<tr>
<td width="50%"><img src="docs/images/save-backups.webp" alt="存档备份"><br><sub><b>存档备份</b>：备份统计、校验、下载、恢复和删除</sub></td>
<td width="50%"><img src="docs/images/save-index.webp" alt="存档解析"><br><sub><b>存档解析</b>：快照状态、自动解析、格式检测和手动任务</sub></td>
</tr>
</table>

### 通知与外部协作

<table>
<tr>
<td width="50%"><img src="docs/images/notification-channels.webp" alt="消息通知"><br><sub><b>消息通知</b>：多渠道 Webhook、事件订阅、模板与重试策略</sub></td>
<td width="50%"><img src="docs/images/notification-history.webp" alt="推送记录"><br><sub><b>推送记录</b>：投递状态、HTTP 结果、耗时和失败原因</sub></td>
</tr>
</table>

### PalDefender 与系统治理

<table>
<tr>
<td width="50%"><img src="docs/images/system-settings.webp" alt="系统设置"><br><sub><b>系统设置</b>：Palworld、PalDefender、RCON、存档、备份与自动任务</sub></td>
<td width="50%"><img src="docs/images/paldefender-console.webp" alt="防护组件"><br><sub><b>防护组件</b>：PalDefender 连接、版本、配置文件和中文字段说明</sub></td>
</tr>
<tr>
<td width="50%"><img src="docs/images/catalog-management.webp" alt="目录管理"><br><sub><b>目录管理</b>：物品与帕鲁目录、分类、收藏和别名</sub></td>
<td width="50%"><img src="docs/images/audit-log.webp" alt="审计日志"><br><sub><b>审计日志</b>：关键操作、结果、来源地址和结构化详情</sub></td>
</tr>
<tr>
<td width="50%"><img src="docs/images/system-logs.webp" alt="系统日志"><br><sub><b>系统日志</b>：业务级日志、级别筛选和异常定位</sub></td>
<td width="50%"><img src="docs/images/user-management.webp" alt="权限管理"><br><sub><b>权限管理</b>：多角色账号、状态、最近登录和安全操作</sub></td>
</tr>
<tr>
<td width="50%"><img src="docs/images/about-project.webp" alt="关于系统"><br><sub><b>关于系统</b>：版本、参考项目、数据来源和开源声明</sub></td>
<td width="50%"><img src="docs/images/overview-dark.webp" alt="深色主题"><br><sub><b>深色主题</b>：适合长时间服务器运维和夜间监控</sub></td>
</tr>
</table>

## PalDefender 特色集成

```mermaid
flowchart LR
    Admin[管理员] --> PalOps[PalOps Web]
    PalOps -->|REST Bearer Token| PDREST[PalDefender REST]
    PalOps -->|扩展命令| RCON[PalDefender RCON]
    PalOps -->|白名单校验 / 备份 / 原子替换| Config[PalDefender JSON 配置]
    PDREST --> Runtime[玩家 / 指标 / 版本 / 防护数据]
    RCON --> Operations[封禁 / 解封 / 广播 / reloadcfg]
    Config --> Protection[反作弊 / 日志 / 管理员 / 游戏规则]
```

PalOps 提供：

- PalDefender 当前版本与稳定版本对比；
- REST 地址和 Bearer Token 连通性管理；
- `Config.json` 官方字段的中文名称、类型和说明；
- `RESTConfig.json`、Token、WhiteList、BanList、ImportRules、PalTemplate、PalSummon 的生成与管理；
- JSON 结构校验、SHA-256 并发检查、修改前备份、临时文件写入和原子替换；
- 需要 `reloadcfg` 或重启时的明确提示；
- PalDefender 扩展 RCON 能力探测与高风险确认。

安装与 REST Token 配置见 [PalDefender 部署说明](docs/paldefender-deployment.md)。

## 系统架构

```mermaid
flowchart LR
    Browser[浏览器\nVue 3 + Element Plus + MapLibre] -->|Cookie + CSRF| Web[PalOps.Web\nASP.NET Core .NET 10]
    Web -->|SignalR| Browser
    Web --> Runtime[PalServer / Shipping\nWindows 进程控制]
    Web --> Save[Level.sav / Players\n只读快照解析]
    Web --> PalREST[Palworld REST]
    Web --> RCON[Palworld / PalDefender RCON]
    Web --> Defender[PalDefender REST 与配置目录]
    Web --> Notify[Webhook 渠道]
    Web --> Data[(本地 JSON / JSONL\n备份、审计和设置)]
    Browser --> Map[前端离线地图瓦片\nPOI 与坐标转换]
```

关键安全边界：

- 浏览器不直接操作 PalServer、RCON 或 PalDefender Token；
- 所有写操作经过身份、角色权限、CSRF 和审计检查；
- 停服和强停前重新验证 PID、可执行文件路径和服务器安装目录；
- 存档解析只读取私有快照，失败时保留最后一份成功索引；
- PalDefender 配置只允许白名单文件和受控相对路径；
- 地图瓦片与固定 POI 完全由前端静态资源提供，后端不执行地图导入或健康检查。

## 快速开始

### 环境要求

- Windows 10/11 或 Windows Server；
- .NET 10 SDK；
- Node.js 22；
- 本机 Palworld Dedicated Server；
- 推荐安装 PalDefender，以启用防护、扩展 RCON 与特色实时数据能力。

### 构建

```powershell
git clone <your-repository-url>
cd PalOpsWeb
.\scripts\build.ps1
```

Windows 发布包：

```powershell
.\scripts\fetch-map-tiles.ps1 -Layer all
.\scripts\publish-win-x64.ps1 -Version 1.1.0
```

### 首次部署

1. 将发布包解压到独立目录；
2. 使用有权读取 Palworld 存档和检查 PalServer 进程的 Windows 账户启动；
3. 完成 Owner 初始化；
4. 配置 Palworld REST、RCON 和 PalDefender REST；
5. 配置存档、备份目录和自动解析；
6. 验证玩家、公会、地图、物资发放、通知和 PalDefender 配置；
7. 只通过受信任 LAN、VPN 或 HTTPS 反向代理开放管理入口。

完整说明见 [构建文档](docs/build.md) 和 [部署文档](docs/deployment.md)。

## 文档

默认文档为中文，每份公开技术文档提供 English 对应页。

| 主题 | 中文 | English |
|---|---|---|
| 文档首页 | [docs/README.md](docs/README.md) | [docs/README.en.md](docs/README.en.md) |
| 架构 | [architecture.md](docs/architecture.md) | [architecture.en.md](docs/architecture.en.md) |
| 构建 | [build.md](docs/build.md) | [build.en.md](docs/build.en.md) |
| 部署 | [deployment.md](docs/deployment.md) | [deployment.en.md](docs/deployment.en.md) |
| PalDefender 部署 | [paldefender-deployment.md](docs/paldefender-deployment.md) | [paldefender-deployment.en.md](docs/paldefender-deployment.en.md) |
| PalDefender 配置 | [paldefender-configuration-management.md](docs/paldefender-configuration-management.md) | [paldefender-configuration-management.en.md](docs/paldefender-configuration-management.en.md) |
| 地图数据 | [world-map-data-1.1.0.md](docs/world-map-data-1.1.0.md) | [world-map-data-1.1.0.en.md](docs/world-map-data-1.1.0.en.md) |
| 发布检查 | [release-checklist.md](docs/release-checklist.md) | [release-checklist.en.md](docs/release-checklist.en.md) |

## GitHub Actions

`.github/workflows/build.yml` 在 push、Pull Request 和手动触发时执行：

1. Node.js 22 依赖安装、前端契约、TypeScript 和 Vite 构建；
2. npm 高危依赖审计；
3. .NET 10 restore 与 Release build；
4. 目录、地图、文档和源码仓库校验；
5. Python 源文件、日文 Markdown、运行数据、密钥和编译缓存残留检查。

项目运行、构建和发布不依赖 Python。

## 安全、贡献与许可证

- 不要把 PalOps、Palworld REST、PalDefender REST 或 RCON 端口直接暴露到公网；
- 不要提交 `data`、存档、数据库、日志、Data Protection 密钥、密码、Token 或 Cookie；
- 生命周期控制只支持 Windows 本机 PalServer；
- Web UI 以桌面浏览器为目标；
- 地图图像再分发前应核对 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

漏洞报告见 [SECURITY.md](SECURITY.md)，贡献流程见 [CONTRIBUTING.md](CONTRIBUTING.md)。

PalOps Web 以 **GNU GPL v3 或更高版本**发布。Palworld 及相关名称、商标和游戏资产归各自权利人所有；本项目与 Pocketpair 无隶属或背书关系。
