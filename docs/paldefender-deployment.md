# PalDefender 部署与 PalOps Web 接入指南

> 语言：**简体中文** | [English](paldefender-deployment.en.md)

## 1. 适用范围

PalOps Web 的“防护组件”、PalDefender 版本检测、REST 数据聚合以及配置文件管理都依赖本机 PalServer 中已经正确部署并启动的 PalDefender。PalOps Web **不会代替 PalDefender 注入游戏进程**，也不会自动下载或覆盖 PalDefender 的核心 DLL。

推荐拓扑：

```text
管理浏览器
  └─ PalOps Web（与 PalServer 同一台 Windows 主机）
       ├─ PalServer-Win64-Shipping.exe
       ├─ Palworld 官方 REST API
       ├─ RCON
       └─ PalDefender
            ├─ REST API
            └─ Pal/Binaries/Win64/PalDefender/*.json
```

PalOps Web、PalServer 和需要管理的 PalDefender 配置目录必须位于同一台 Windows 主机。Linux PalServer 需要 Wine/Proton 等兼容层，PalOps Web 当前不负责该兼容层的安装、进程注入或生命周期控制。

## 2. 安装前准备

1. 停止 PalServer，并确认 `PalServer-Win64-Shipping.exe` 已完全退出。
2. 备份完整的 Palworld 世界存档。
3. 已安装 PalDefender 时，额外备份：

```text
Pal/Binaries/Win64/PalDefender
Pal/Binaries/Win64/PalDefender/.palops-backups
Pal/Binaries/Win64/PalDefender.dll
Pal/Binaries/Win64/d3d9.dll
```

4. 从 PalDefender 官方发行页获取与当前 Palworld/PalServer 版本兼容的 Windows ZIP。
5. 不要从不明镜像下载 DLL，不要在服务器运行中覆盖注入文件。

## 3. 安装 PalDefender

将官方 ZIP 中的下列文件复制到 PalServer 的 Win64 目录：

```text
Pal/Binaries/Win64/PalDefender.dll
Pal/Binaries/Win64/d3d9.dll
```

目标目录示例：

```text
D:\PalServer\Pal\Binaries\Win64\
```

首次启动 PalServer 后，PalDefender 会在同一 Win64 目录下创建配置目录。正常目录结构应类似：

```text
Pal/Binaries/Win64/
├─ PalServer-Win64-Shipping.exe
├─ PalDefender.dll
├─ d3d9.dll
└─ PalDefender/
   ├─ Config.json
   ├─ WhiteList.json
   ├─ Banlist.json
   ├─ Pals/
   │  ├─ ImportRules/
   │  ├─ Templates/
   │  └─ Summons/
   └─ RESTAPI/
      ├─ RESTConfig.json
      └─ Tokens/
         └─ <实际令牌文件>.json
```

启动控制台应能看到 PalDefender 加载、配置读取和 REST API 启动信息。目录未生成时，不要先在 PalOps Web 中手工拼凑完整目录；应先排查 DLL 路径、文件被安全软件拦截、版本不兼容或 PalDefender 未加载。

## 4. 区分两套 REST API

PalOps Web 可以同时使用两套不同接口：

| 接口 | 常见用途 | 配置位置 |
|---|---|---|
| Palworld 官方 REST API | `/info`、`/metrics`、`/players` 等服务器运行数据 | Palworld 服务端启动参数/配置 |
| PalDefender REST API | PalDefender 版本、扩展玩家数据、处罚和防护能力 | `PalDefender/RESTAPI/RESTConfig.json` 与 `RESTAPI/Tokens/*.json` |

两套端口、认证方式和权限彼此独立。不要把 Palworld REST Token 填入 PalDefender Token，也不要把 PalDefender REST 地址当作 Palworld 官方 REST 地址。

## 5. 配置 PalDefender REST API

### 5.1 RESTConfig.json

在以下文件启用并设置监听端口：

```text
Pal/Binaries/Win64/PalDefender/RESTAPI/RESTConfig.json
```

可先通过 PalOps Web“防护组件 → 配置文件 → 新建配置”生成草稿。典型内容：

```json
{
  "Enabled": true,
  "Port": 17993
}
```

修改 RESTConfig 后需要重启 PalServer。端口必须未被其他程序占用。

### 5.2 创建实际 Token

在以下目录创建实际 Token 文件：

```text
Pal/Binaries/Win64/PalDefender/RESTAPI/Tokens/PalOps.json
```

示例结构：

```json
{
  "Name": "PalOps",
  "Token": "请替换为高强度随机令牌",
  "Permissions": [
    "REST.*"
  ]
}
```

注意：

- `TokenExample.json` 只是示例文件，不会被当作可用 Token；不要在 PalOps Web 中填写其示例值。
- PalOps Web 请求使用 HTTP 头 `Authorization: Bearer <token>`。
- Token 按密码管理，不要写入截图、Issue、普通系统日志或版本库。
- 生产环境应按服务拆分 Token，并按实际接口收窄权限；只有确认需要完整管理能力时才使用 `REST.*`。
- Token 或权限配置变更后应重启 PalServer，再执行连接测试。

## 6. 在 PalOps Web 中配置

进入“系统设置”或当前版本对应的服务器连接配置区域，分别填写：

1. Palworld 官方 REST 基础地址和认证信息。
2. PalDefender REST 基础地址，例如：

```text
http://127.0.0.1:17993
```

3. PalDefender Token，填写 Token JSON 中的 `Token` 字段值，不要附加 `Bearer ` 前缀，除非界面明确要求完整 Header。
4. RCON 地址、端口和密码。
5. PalServer 安装目录或启动脚本，供版本检测、配置目录定位和生命周期控制使用。

建议优先使用 `127.0.0.1` 或专用内网地址。PalOps Web 与 PalServer 同机时，没有必要把 PalDefender REST 端口开放到公网。

完成后依次验证：

1. Palworld REST `/info` 与 `/metrics` 连接测试成功。
2. PalDefender 版本检测能显示当前版本。
3. PalDefender 配置文件页面能列出 `Config.json`、REST 配置和实际 Token 文件。
4. 玩家列表或地图服务器数据能够按配置聚合。
5. 保存一个非生产测试字段后，可通过 `reloadcfg` 或重启验证生效。

## 7. Windows 防火墙与网络边界

推荐策略：

- PalDefender REST 仅监听环回地址时，不创建外部防火墙放行规则。
- 必须从管理内网访问时，只允许明确的管理网段和 TCP 端口。
- 不要把 Palworld REST、PalDefender REST、RCON 或 PalOps 管理端口直接暴露到公网。
- 跨网络管理使用 VPN 或配置正确的 HTTPS 反向代理；PalDefender REST 本身仍建议保持内网/本机可见。
- 检查 Windows 防火墙、云安全组、路由器端口映射三处规则，避免“只改一处但仍暴露/仍不通”。

## 8. PalOps 配置文件管理权限

PalOps Web 进程账户至少需要读取：

```text
Pal/Binaries/Win64/PalDefender
```

启用在线保存、生成、备份或删除扩展配置时，还需要对以下范围具有创建、写入、重命名和删除权限：

```text
Pal/Binaries/Win64/PalDefender/*.json
Pal/Binaries/Win64/PalDefender/Pals/**
Pal/Binaries/Win64/PalDefender/RESTAPI/**
Pal/Binaries/Win64/PalDefender/.palops-backups/**
```

不要为了省事给整个 PalServer 安装目录授予 Everyone 完全控制。详细的文件白名单、SHA-256 并发控制、原子写入和备份规则见 [`paldefender-configuration-management.md`](paldefender-configuration-management.md)。

## 9. 升级 PalDefender

1. 在 PalOps Web 中执行安全停服，并确认 PalServer 进程完全退出。
2. 备份世界存档和完整 `PalDefender` 配置目录。
3. 记录当前 PalDefender 版本、PalServer 版本、REST 端口和 Token 文件。
4. 将新版本解压到临时目录，核对发行说明和文件清单。
5. 替换官方要求更新的 `PalDefender.dll`、`d3d9.dll` 及配套文件；不要删除现有配置后再盲目重建。
6. 启动非生产或维护窗口中的 PalServer，检查 PalDefender 启动日志。
7. 在 PalOps Web 中依次验证版本、REST、玩家数据、配置读取、RCON 和地图服务器标记。
8. 确认无误后再删除旧 DLL 备份。

不要在 PalServer 运行时热替换注入 DLL。

## 10. 回滚

出现启动失败、REST 403、行为异常或游戏更新后不兼容时：

1. 停止 PalServer。
2. 保存新版本产生的日志，便于诊断。
3. 恢复升级前备份的 `PalDefender.dll`、`d3d9.dll` 和 `PalDefender` 配置目录。
4. 检查文件是否被 Windows 标记为来自 Internet，必要时在确认来源可信后解除阻止。
5. 重新启动 PalServer，确认控制台显示旧版本正常加载。
6. 在 PalOps Web 中重新执行连接测试和版本检测。

如果 Palworld 游戏版本已经改变且旧 PalDefender 也不兼容，应保持 PalDefender 停用并等待兼容发行版，而不是反复覆盖 DLL。

## 11. 常见故障

### PalDefender 目录没有生成

- 确认 DLL 位于 `Pal/Binaries/Win64`，不是 PalServer 根目录。
- 确认 `PalDefender.dll` 和 `d3d9.dll` 均来自同一官方发行包。
- 查看 PalServer 启动控制台是否出现 PalDefender 加载行。
- 检查杀毒软件隔离记录和文件权限。
- 核对 Palworld 更新后是否需要新版 PalDefender。

### REST 返回 401

- 检查请求是否使用 `Authorization: Bearer <token>`。
- 检查填写的是 Token 字段值，而不是文件名或 Name。
- 不要使用 `TokenExample.json`。
- 检查 JSON 是否为严格合法格式，然后重启 PalServer。

### REST 返回 403

- Token 已识别，但权限不足，检查 `Permissions`。
- 确认目标接口所需权限已授权。
- 修改权限后重启 PalServer，再重新测试。

### 连接被拒绝或超时

- 确认 PalDefender 日志显示 REST API 已启动及实际端口。
- 使用 `netstat -ano` 或 PowerShell 检查端口监听与占用。
- 确认 PalOps 中使用的是 PalDefender 端口，而不是 Palworld REST 端口。
- 同机部署优先测试 `127.0.0.1`。
- 检查防火墙规则是否限制了错误的程序、端口或网段。

### PalOps 能读配置但保存失败

- PalOps 运行账户缺少创建、重命名或删除权限。
- 配置文件被其他编辑器独占。
- SHA-256 冲突表示文件已被外部修改，应重新加载，不要强行覆盖。
- Windows reparse point、符号链接或越界路径会被安全策略拒绝。

### 控制台字符或日志异常

- 优先保留 PalDefender 原始控制台和文件日志。
- PalOps 系统日志默认不记录成功的底层 HttpClient 请求生命周期，只保留 Warning/Error；连接测试失败时查看对应错误、状态码和 PalDefender 日志。
- 不要为排错长期启用全局 HTTP Information/Trace 日志，避免日志快速增长和敏感 Header 风险。

## 12. 部署后检查清单

- [ ] PalDefender 控制台显示加载成功。
- [ ] `PalDefender` 配置目录已生成。
- [ ] Palworld 官方 REST 与 PalDefender REST 地址未混用。
- [ ] 实际 Token 文件不是 `TokenExample.json`。
- [ ] PalOps 版本检测正常。
- [ ] PalOps 配置文件读取和校验正常。
- [ ] 玩家、处罚、防护或地图所需的扩展数据可读取。
- [ ] RCON `reloadcfg` 或重启后配置生效。
- [ ] 防火墙仅允许本机或受信任管理网段。
- [ ] 世界存档、PalOps `data` 和 PalDefender 配置均有可恢复备份。

## 13. 官方参考

- PalDefender 安装：`https://ultimeit.github.io/PalDefender/zh/Installation/`
- PalDefender 文件类型：`https://ultimeit.github.io/PalDefender/zh/FileTypes/`
- Config.json：`https://ultimeit.github.io/PalDefender/zh/FileTypes/Config/`
- REST API 认证：`https://ultimeit.github.io/PalDefender/zh/RESTAPI/authentication/`
- 常见问题：`https://ultimeit.github.io/PalDefender/zh/FAQ/`