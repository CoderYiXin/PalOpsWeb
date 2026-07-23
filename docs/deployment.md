# 部署指南

> 语言：**简体中文** | [English](deployment.en.md)

## 支持拓扑

PalOps Web、PalServer、目标世界存档和可选 PalDefender 必须位于同一台 Windows 主机。浏览器可以通过受信任 LAN、VPN 或 HTTPS 反向代理访问。

不支持：

- 远程 Agent 控制另一台主机的 PalServer；
- Linux 本机生命周期控制；
- 通过 UNC 路径发现远程进程；
- 浏览器直接访问 RCON、Palworld REST 或 PalDefender REST。

## 部署前准备

- Windows 10/11 或 Windows Server；
- 已安装 .NET 10 Runtime，或使用自包含发布包；
- 具有存档读取、PalOps 数据目录写入和 PalServer 进程检查权限的账户；
- 确认 Palworld 世界存档目录；
- 可选的 Palworld REST、RCON 和 PalDefender REST 参数；
- 反向代理场景下的 HTTPS 证书和 WebSocket 支持。

## 首次部署

1. 将发布包解压到独立目录，例如 `D:\PalOpsWeb\1.3.0`；
2. 不要直接覆盖旧版本目录；
3. 运行 `start.cmd` 或 `PalOps.Web.exe`；
4. 从控制台读取监听地址；
5. 打开浏览器并初始化 Owner；
6. 确认存档路径、服务器根目录、启动脚本和 EXE；
7. 配置 Palworld REST / RCON；
8. 可选配置 PalDefender REST 地址和 Token；
9. 验证存档索引、玩家、公会、据点、地图和通知渠道。

## 数据目录

必须持久化：

- PalOps `data`；
- ASP.NET Core Data Protection 密钥；
- 用户希望保留的备份；
- PalDefender 配置与 `.palops-backups`；
- 根据许可证允许分发的离线地图数据。

不要将上述运行数据提交到 Git。

## PalServer 生命周期权限

运行 PalOps 的 Windows 账户需要：

- 读取 PalServer 安装目录；
- 查询 PalServer / Shipping 进程；
- 在确认的安装目录内启动脚本或 EXE；
- 正常停止确认成功后终止经过身份验证的残留进程。

正常停止流程为：

```text
验证进程身份
→ Shutdown 1 Server will shut down in 1 seconds
→ 确认 RCON 成功
→ 10 秒实时倒计时
→ 检查是否完全退出
→ 强停剩余已验证 PID
→ 清理 PalServer.exe 与竞态 Shipping
→ 完成
```

## 反向代理

反向代理必须：

- 使用 HTTPS；
- 转发真实 Host 和受信任代理头；
- 支持 `/hubs/palops` WebSocket Upgrade；
- 设置足够的上传/请求限制；
- 只信任明确配置的代理地址。

不要把 Palworld REST、PalDefender REST 或 RCON 端口暴露给浏览器。

## 防火墙

只开放 PalOps 的 HTTPS/LAN 监听端口。Palworld REST、PalDefender REST 和 RCON 建议仅绑定回环或受信任管理网段。

## 升级

1. 备份世界存档、PalOps `data`、Data Protection 密钥、备份目录和 PalDefender 配置；
2. 解压新版本到新目录；
3. 停止旧 PalOps；
4. 复制受保护数据，不复制旧程序文件；
5. 启动新版本；
6. 验证登录、存档索引、进程身份、地图、通知和 PalDefender；
7. 保留旧目录直至验收完成。

## 回滚

1. 停止新版本；
2. 恢复与旧版本兼容的数据备份；
3. 启动旧目录；
4. 验证登录、存档索引和生命周期；
5. 不要在未知数据格式上反复切换版本。

## 部署后检查

- 控制台不输出密码、Token 或 Cookie；
- Owner、Administrator、Operator、Auditor、Viewer 权限正确；
- 自动索引间隔不低于 10 分钟，无人在线时跳过重复解析；
- 正常停止显示 10→1 秒倒计时并能自动清理残留；
- 世界地图默认服务器数据、快速传送和高塔可在首次加载中出现；
- SignalR 实时事件正常；
- 健康 HTTP 请求不会刷屏，错误请求仍有日志；
- PalDefender 版本、REST 和配置管理按预期工作。

PalDefender 的独立安装和 REST 接入步骤见 [PalDefender 部署指南](paldefender-deployment.md)。
