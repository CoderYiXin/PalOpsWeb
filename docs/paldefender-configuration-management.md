# PalDefender 配置文件管理

> 语言：**简体中文** | [English](paldefender-configuration-management.en.md)

## 目标与边界

“防护组件 → PalDefender 配置文件”用于在 PalOps Web 中读取、生成、校验和安全修改同机 PalServer 安装目录下的 PalDefender JSON 配置。管理根目录固定为：

```text
Pal/Binaries/Win64/PalDefender
```

PalOps 根据当前 PalServer 启动配置定位该目录，只开放明确列入服务端白名单的 JSON 文件。该功能是本地文件管理器，不通过 PalDefender REST API 反向修改配置，也不允许浏览任意服务器目录。

PalDefender 核心 DLL 安装、REST Token、Windows 防火墙、升级与回滚请先阅读 [`paldefender-deployment.md`](paldefender-deployment.md)。

官方说明：

- `https://ultimeit.github.io/PalDefender/zh/FileTypes/`
- `https://ultimeit.github.io/PalDefender/zh/FileTypes/Config/`
- `https://ultimeit.github.io/PalDefender/zh/FileTypes/PalImportRules/`
- `https://ultimeit.github.io/PalDefender/zh/FileTypes/PalTemplate/`
- `https://ultimeit.github.io/PalDefender/FileTypes/PalSummon/`
- `https://ultimeit.github.io/PalDefender/zh/RESTAPI/authentication/`

## 支持的文件

| 路径 | 类型 | 可删除 | 生效提示 |
|---|---|---:|---|
| `Config.json` | 主配置 | 否 | `reloadcfg` 或重启服务器 |
| `WhiteList.json` | 白名单 | 否 | `reloadcfg` |
| `Banlist.json` | 封禁列表 | 否 | `reloadcfg` |
| `Pals/ImportRules/*.json` | Pal 导入规则 | 是 | `reloadcfg`，未生效则重启 |
| `Pals/Templates/*.json` | Pal 模板 | 是 | `reloadcfg`，未生效则重启 |
| `Pals/Summons/*.json` | Pal 召唤配置 | 是 | `reloadcfg`，未生效则重启 |
| `RESTAPI/RESTConfig.json` | REST API 监听配置 | 否 | 重启服务器 |
| `RESTAPI/Tokens/*.json` | REST API Bearer 令牌 | 是 | 重启服务器 |

`RESTAPI/Tokens/TokenExample.json` 是 PalDefender 示例文件，不会被当作有效令牌，因此 PalOps 不列出、不生成、也不允许修改该名称。只允许 `.json` 文件，单文件最大 2 MiB。

## 中文字段提示与“注释”说明

PalDefender 配置文件必须保持标准 JSON。JSON 不允许注释或末尾多余逗号，因此 PalOps 不会把 `//`、`/* */` 或 `_comment` 字段写进真实配置，以免 PalDefender 加载失败。

结构化编辑器通过独立元数据为每个官方字段显示：

- 中文字段名称；
- 中文说明；
- 原始 JSON 键名；
- 字段类型和分组；
- 默认值；
- 废弃字段警告；
- 英文、日文对应说明。

当前元数据覆盖：

- `Config.json` 官方列出的 76 个字段；
- REST API 的 `Enabled`、`Port`、`Name`、`Token`、`Permissions`；
- PalTemplate 的全部官方字段；
- PalSummon 的全部官方字段，包括 `DisableStatuses`；
- ImportRules 的全部官方字段；
- WhiteList/Banlist 的 User ID 与 IP 标识说明。

因此页面上具备注释式说明，但保存到磁盘的仍是合法、无注释 JSON。

## REST API 配置生成与管理

“新建配置”支持两种 REST API 文件：

### RESTConfig.json

生成路径：

```text
RESTAPI/RESTConfig.json
```

默认内容：

```json
{
  "Enabled": false,
  "Port": 17993
}
```

`Port` 必须在 `1-65535` 范围内。修改该文件后需要重启 PalServer。不要把 REST API 端口直接暴露到公网。

### REST API Token

生成路径：

```text
RESTAPI/Tokens/<安全文件名>.json
```

默认生成 32 字节随机令牌，并使用完整权限示例：

```json
{
  "Name": "PalOps",
  "Token": "<64 位十六进制随机值>",
  "Permissions": [
    "REST.*"
  ]
}
```

`Permissions` 可为字符串或字符串数组；`REST.*` 表示完整 REST 权限。生产环境应按人或服务拆分令牌并收窄权限。令牌按密码处理，不应写入系统日志、审计详情或截图。

## 文件生成模板

PalOps 可生成以下合法初始模板：

- `Config.json`
- `WhiteList.json`
- `Banlist.json`
- `RESTAPI/RESTConfig.json`
- `RESTAPI/Tokens/*.json`
- `Pals/ImportRules/*.json`
- `Pals/Templates/*.json`
- `Pals/Summons/*.json`

生成操作只返回草稿，不会直接覆盖磁盘。管理员必须检查相对路径和内容，再执行保存。已有文件的路径会触发覆盖确认和 SHA-256 并发保护。

## 权限

- 所有已登录用户可读取文件列表、内容与字段元数据。
- `Owner` 与 `Administrator` 可生成草稿、校验、保存、新建和删除扩展配置。
- 保存与删除操作要求 CSRF 校验并写入审计日志。
- 核心文件 `Config.json`、`WhiteList.json`、`Banlist.json`、`RESTAPI/RESTConfig.json` 不允许从 PalOps 删除。

前端角色判断仅控制界面可用性，最终权限由后端策略决定。

## 编辑模式

编辑器提供两种视图：

1. **结构化模式**：按字段元数据显示名称、说明、类型、分组和对应输入控件；可从“已知字段”列表补充缺失字段。
2. **原始 JSON 模式**：适合复杂嵌套对象或需要精确检查格式的场景。

两种模式编辑同一份 JSON 草稿。保存前始终调用服务端校验，不以浏览器校验结果代替服务端判断。

## 校验规则

服务端先执行严格 JSON 解析，然后按文件类型应用规则：

- 拒绝空内容、JSON 注释、末尾逗号、非法 JSON、非有限数值和错误根节点类型。
- `Config.json` 校验已知布尔、整数、数字、字符串数组，并对废弃字段给出警告。
- `WhiteList.json` 与 `Banlist.json` 校验字符串标识符。
- PalTemplate 要求 `PalID`，并校验性别、等级、伙伴技能等级、状态、技能、被动、IV、Pal Souls 和工作适应性等字段。
- PalSummon 要求 `PalTemplate` 与有限的 `X/Y/Z` 坐标；`Uncapturable` 必须为布尔值，`DisableStatuses` 必须为字符串数组，未知状态给出警告。
- ImportRules 校验选择模式、超限处理、禁止被动处理、等级/Rank、布尔开关、数组、IV 和 Pal Souls。
- RESTConfig 要求 `Enabled` 与有效端口；令牌要求 `Name`、`Token` 和至少一个 `Permissions`。

校验响应分为 `error`、`warning` 和 `info`。存在任何 `error` 时禁止保存；`warning` 不阻止保存，但管理员应确认其含义。

## 安全写入流程

```text
读取当前 SHA-256
    ↓
校验 expectedSha256，防止并发覆盖
    ↓
再次解析并校验 JSON
    ↓
复制旧文件到 .palops-backups
    ↓
在同目录写入临时文件
    ↓
FlushAsync + Flush(flushToDisk: true)
    ↓
同卷原子替换目标文件
```

备份目录位于：

```text
Pal/Binaries/Win64/PalDefender/.palops-backups
```

备份按原相对路径和时间戳组织。审计记录只保存文件相对路径、类型、大小和操作者，不记录完整配置或 REST Token。

## 路径安全

服务端执行以下限制：

- 路径标准化后必须位于 PalDefender 根目录内；
- 拒绝绝对路径、盘符、`..`、非 JSON 扩展名和未知目录；
- 根目录和路径上的现有组件不得为符号链接或 Windows reparse point；
- 扩展目录只允许直接子文件，不递归读取任意层级；
- 拒绝把 `TokenExample.json` 当作有效令牌管理。

## API

```text
GET    /api/v1/paldefender/config-files
GET    /api/v1/paldefender/config-files/metadata?kind=<kind>
POST   /api/v1/paldefender/config-files/generate
GET    /api/v1/paldefender/config-files/content?path=<relative-path>
POST   /api/v1/paldefender/config-files/validate
PUT    /api/v1/paldefender/config-files/content?path=<relative-path>
DELETE /api/v1/paldefender/config-files/content?path=<relative-path>&expectedSha256=<sha256>
```

写入请求使用 `expectedSha256` 实现乐观并发控制。文件被 PalDefender、管理员或其他进程修改后，旧页面不能直接覆盖新内容，必须重新加载。

## 部署与烟雾测试

PalOps Web 进程账户必须对 PalDefender 目录具有读取权限；在线保存时还必须具有创建、重命名和删除文件的权限。建议只授予该目录及 `.palops-backups` 所需权限。

发布后至少验证：

1. 能列出主配置、Pals 子目录、RESTConfig 与有效 Token 文件。
2. Viewer 只能读取；Administrator 可以生成、校验和保存。
3. 每个官方字段在结构化编辑器中显示中文名称与中文说明。
4. 非法 JSON、错误字段类型、路径穿越和 `TokenExample.json` 被拒绝。
5. 并发修改触发 SHA-256 冲突提示。
6. 保存前自动备份，保存后 JSON 可重新读取。
7. RESTConfig 与 Token 变更在重启后生效；其他配置按页面提示执行 `reloadcfg` 或重启。