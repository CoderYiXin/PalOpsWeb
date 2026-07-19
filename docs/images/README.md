# 文档图片与演示截图

> 语言：**简体中文** | [English](README.en.md)

本目录保存 GitHub README 和公开文档使用的产品截图。所有截图均来自当前 PalOps Web 前端构建，并使用本地脱敏演示数据生成；服务器、账户、玩家、公会、路径、坐标、Token、Webhook 和操作结果均为虚构值。地图使用仓库内自带的离线瓦片和 POI 数据，不包含真实服务器响应。

## 功能模块截图

| 文件 | 尺寸 | 对应模块 | 公开用途 |
|---|---:|---|---|
| `overview-dashboard.webp` | 1920×1080 | 运行概览 | README 首图；展示 PalServer、主机、存档、PalDefender 和事件态势 |
| `player-management.webp` | 1920×1080 | 玩家管理 | 在线/离线玩家、角色属性、背包与帕鲁档案 |
| `guild-bases.webp` | 1920×1080 | 公会据点 | 公会成员、据点归属证据和地图定位 |
| `world-map.webp` | 1920×1080 | 世界地图 | Palpagos / World Tree、固定 POI、玩家、据点和自定义标记 |
| `resource-grant.webp` | 1920×1080 | 物资发放 | 多目标选择、资源搜索、任务购物车和执行结果 |
| `message-center.webp` | 1920×1080 | 消息发送 | 公告、警告、私聊和玩家选择 |
| `rcon-console.webp` | 1920×1080 | RCON 指令控制 | 标准/PalDefender 指令、风险识别和响应历史 |
| `automation-jobs.webp` | 1920×1080 | 自动任务 | 周期任务、风险等级、下次执行和历史结果 |
| `save-backups.webp` | 1920×1080 | 存档备份 | 备份、SHA-256 校验、下载、恢复和删除 |
| `notification-channels.webp` | 1920×1080 | 消息通知 | 多渠道 Webhook、事件订阅、模板和重试策略 |
| `notification-history.webp` | 1920×1080 | 推送记录 | 投递状态、HTTP 结果、耗时和失败原因 |
| `system-settings.webp` | 1920×1080 | 系统设置 | Palworld、PalDefender、RCON、存档、备份和自动任务设置 |
| `paldefender-console.webp` | 1920×1080 | 防护组件 | PalDefender 连接、版本、配置文件与中文字段说明 |
| `save-index.webp` | 1920×1080 | 存档解析 | 快照索引、自动解析、格式检测和失败回退 |
| `catalog-management.webp` | 1920×1080 | 目录管理 | 物品/帕鲁目录、分类、收藏和别名 |
| `audit-log.webp` | 1920×1080 | 审计日志 | 关键操作、结果、来源地址和结构化详情 |
| `system-logs.webp` | 1920×1080 | 系统日志 | 降噪后的业务日志、级别筛选和异常定位 |
| `user-management.webp` | 1920×1080 | 权限管理 | 多角色账号、启停、最近登录和安全操作 |
| `about-project.webp` | 1920×1080 | 关于系统 | 版本、参考项目、数据来源和开源声明 |

## 辅助图片

| 文件 | 尺寸 | 用途 |
|---|---:|---|
| `overview-light.webp` | 1920×1080 | 浅色主题补充展示 |
| `overview-dark.webp` | 1920×1080 | 深色主题和长时间运维场景 |
| `webhook-notifications.webp` | 1920×1080 | 旧版通知配置补充图，保留用于历史文档兼容 |
| `map-icon-catalog.png` | 1020×756 | 地图分类图标目录 |

## 脱敏规则

允许使用保留地址 `192.0.2.10`、域名 `webhook.example.invalid`、虚构玩家名（如 `Aster`、`Birch`）、虚构公会和非用户路径 `C:\PalOpsDemo\...`。

替换或新增图片前必须确认不包含：

- 真实 IP、域名、服务器端口或公网入口；
- Windows 用户名、安装目录、存档目录或备份目录；
- 玩家 UID、SteamID、公会真实名称或聊天内容；
- Cookie、Token、RCON 密码、PalDefender 密钥；
- Webhook 密钥、签名 URL、二维码或请求头；
- 浏览器收藏夹、通知、账户头像等无关个人信息。

模糊处理不能替代删除底层敏感值。公开截图必须使用专门的演示配置和虚构数据重新生成。
