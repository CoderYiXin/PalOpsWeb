# 架构说明

> 语言：**简体中文** | [English](architecture.en.md)

## 支持的部署边界

PalOps Web 是单体 ASP.NET Core .NET 10 应用，同时托管编译后的 Vue 单页应用。正式支持的拓扑是 **Windows 本机部署**：PalOps Web、`PalServer.exe`、目标世界存档和可选 PalDefender 位于同一台 Windows 主机。

```text
桌面浏览器
    │ Cookie + CSRF HTTP
    │ SignalR 状态与事件
    ▼
PalOps.Web (.NET 10)
    ├─ wwwroot 中的 Vue 静态资源
    ├─ Palworld REST 与 TCP RCON 客户端
    ├─ PalDefender REST 与版本检查
    ├─ 存档快照、解压、解析和领域投影
    ├─ Windows PalServer 发现与进程控制
    ├─ 指标、健康、备份、自动任务、审计和通知
    └─ 本地版本化 JSON / JSONL 持久化
```

不支持远程进程 Agent、Linux 进程控制、UNC 进程发现或浏览器直接控制 PalServer。Webhook 仅是出站通知渠道，不是受信任命令入口。

## 应用组合根

`src/PalOps.Web/Program.cs` 负责：

- Cookie 登录、CSRF、角色策略和 API 分组；
- 设置、审计、备份、自动任务、运行配置、通知和存档索引存储；
- 玩家、物资发放和管理服务；
- 文件日志、启动诊断、存档监控、自动任务、运行监控、指标、在线状态与 Webhook 后台服务；
- `/hubs/palops` SignalR Hub；
- `src/PalOps.Web/wwwroot` 下的 Vue 应用。

Endpoint 仅编排接口，不直接执行文件系统、进程、RCON 或外部 HTTP 操作。

## 身份认证、授权和审计

角色由后端策略最终判定：

- **Owner**：全部能力，包括敏感配置与强制停止；
- **Administrator**：常规服务器生命周期和通知管理；
- **Operator**：批准的日常运维操作；
- **Auditor**：只读查看运行、投递和审计信息；
- **Viewer**：基础只读兼容角色。

所有变更记录操作者、动作、结果、原因和时间。密码、Token、Cookie 和配置正文不会写入审计或系统日志。

## 存档解析

存档流水线只读访问 Palworld 原始文件：

```text
解析并保护世界路径
→ 等待 Level.sav 与 Players 目录稳定
→ 创建私有快照
→ 计算源文件哈希
→ 解包 SAV 容器
→ 解压 PlZ / PlM1
→ 有界解析 GVAS 与 RawData
→ 投影玩家、公会、据点、容器和物品领域
→ 关联稳定标识
→ 完整校验
→ 原子发布新索引
```

`PlM1` 通过 Wasmtime 调用内置 `ooz.wasm`。解析过程限制文件大小、递归深度、集合数量和边界。失败结果只记录诊断，不覆盖最后一次成功索引。

自动索引默认最短 10 分钟；已有成功索引且在线人数明确为 0 时跳过本轮解析。手动解析不受该限制。

## 世界地图

固定地图是纯前端边界：

- Palpagos / World Tree 图层配置；
- 682 个本地栅格瓦片；
- 1,251 条三语言 POI；
- 30 个固定分类、图标、搜索别名和坐标变换；
- 浏览器本地探索进度。

后端只返回玩家、公会据点和自定义标记的原始世界坐标或直接地图坐标。浏览器负责图层选择、坐标转换、边界检查和 MapLibre source 更新。

首次进入地图时，服务器数据和固定 POI 并行加载。MapLibre 图层骨架就绪前到达的数据保存在待提交状态，避免底图瓦片加载期间丢失；加载框按引擎、服务器数据、服务器标记、静态数据和静态标记五个阶段显示。

## 公会与据点关联

据点关联按证据强度合并：

1. 据点直接公会/组标识；
2. 公会中的据点 ID 集合；
3. 成员、所有者、工人和建筑相关玩家标识；
4. 可恢复位置的地图或据点核心对象。

据点按稳定 ID 去重；没有 ID 时使用规范化回退身份。位置有效但公会未解析的据点保留为“未归属”，不会丢弃。

## PalServer 生命周期

核心边界是 `IPalServerRuntimeCoordinator`。相关服务负责发现安装目录、定位真实 Shipping 进程、验证 PID、启动脚本、收集指标和保存操作历史。

正常停止和重启的停止阶段严格执行：

```text
重新验证 PalServer / Shipping 身份
→ RCON: Shutdown 1 Server will shut down in 1 seconds
→ 确认业务响应成功
→ 页面实时显示 10 到 1 秒倒计时
→ 若已完全退出则完成
→ 否则重新验证并终止原 Shipping PID
→ 清理残留 PalServer.exe 启动器
→ 清理由竞态重新拉起的 Shipping
→ 最终确认停止
```

只有正常 Shutdown 命令确认成功后才进入自动残留清理。所有终止操作都重新验证路径和 PID，避免误杀其他进程。

## RCON

RCON 变更通过 CSRF 保护的 HTTP Endpoint 执行。高危命令不再要求输入固定 `CONFIRM` 文本，只要求勾选“已核对目标与参数并理解风险”；后端仍校验角色、确认布尔值和命令格式，并记录审计。

## PalDefender

PalOps Web 通过独立 REST 客户端访问 PalDefender：

- 查询版本、玩家和指标；
- 管理 REST 地址与 Bearer Token；
- 读取、生成、校验和保存白名单允许的配置文件；
- 使用 SHA-256 并发控制、备份、临时文件和原子替换；
- 保存后提示 `reloadcfg` 或重启。

配置正文和 Token 不写入日志。路径必须位于已确认的 `Pal/Binaries/Win64/PalDefender` 目录，拒绝穿越、重解析点和未知文件类型。

## 通知与出站 HTTP

通知系统支持通用 JSON、企业微信、钉钉、飞书、Discord、Slack 和 Telegram。渠道机密加密保存，投递历史只保留脱敏元数据。

成功的 `System.Net.Http.HttpClient.*` 请求生命周期信息不写入控制台和系统日志；超时、连接失败、非成功状态码和异常仍保留为 Warning/Error。

## 持久化与并发

PalOps 使用本地 JSON / JSONL 存储设置、索引、审计、历史和通知状态。写入采用进程内锁、临时文件、Flush 和原子替换。大对象与运行时数据不会嵌入前端配置。

## 可观测性

- 控制台与滚动文件日志；
- 系统日志页面；
- 审计日志；
- SignalR 事件；
- 服务器操作快照；
- 备份和通知投递历史。

默认实时状态刷新间隔为 10 秒，并复用短时缓存和在途请求，避免重复请求上游接口。

PalDefender 的安装、REST Token、网络边界和升级回滚见 [PalDefender 部署指南](paldefender-deployment.md)。
