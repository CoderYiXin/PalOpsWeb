# 文档图片与双语产品截图

> 语言：**简体中文** | [English](README.en.md)

`zh-CN/` 保存中文 README 使用的界面，`en-US/` 保存英文 README 使用的界面。两套截图来自同一份 1.2.0 Vue 前端构建，使用相同业务场景、对应语言的伪数据和保留网络地址。不得用宣传海报、合成封面或另一语言截图替代实际页面。

## 中文截图清单

| 文件 | 尺寸 | 模块 | 公开用途 |
|---|---:|---|---|
| `zh-CN/overview-dashboard.webp` | 1920×1080 | 运行概览 | PalServer、主机指标、版本、存档与实时事件集中监控 |
| `zh-CN/server-statistics.webp` | 1920×1080 | 服务器统计 | 玩家、FPS、资源和运营事件趋势 |
| `zh-CN/player-management.webp` | 1920×1080 | 玩家管理 | 在线/离线玩家、角色属性、背包与帕鲁档案 |
| `zh-CN/guild-bases.webp` | 1920×1080 | 公会据点 | 成员、归属证据、据点详情与地图定位 |
| `zh-CN/world-map.webp` | 1920×1080 | 世界地图 | 离线底图、快速传送、实时玩家、据点与自定义标记 |
| `zh-CN/resource-grant-step-1.webp` | 1920×1080 | 物资发放 · 选择玩家 | 按玩家、公会和在线状态选择多个目标 |
| `zh-CN/resource-grant-step-2.webp` | 1920×1080 | 物资发放 · 选择资源 | 分类、多元素搜索、物品/帕鲁选择与批量加入 |
| `zh-CN/resource-grant-step-3.webp` | 1920×1080 | 物资发放 · 核对清单 | 确认目标、资源、数量和最终执行范围 |
| `zh-CN/message-center.webp` | 1920×1080 | 消息发送 | 公告、警告、定向消息和玩家操作 |
| `zh-CN/rcon-console.webp` | 1920×1080 | RCON 控制台 | 命令模板、风险确认、能力探测与返回历史 |
| `zh-CN/palworld-configuration.webp` | 1920×1080 | Palworld 配置中心 | 结构化参数、启动参数、校验与安全保存 |
| `zh-CN/automation-jobs.webp` | 1920×1080 | 自动任务 | 周期任务、风险等级、下次运行和执行历史 |
| `zh-CN/maintenance-center.webp` | 1920×1080 | 维护中心与崩溃守护 | 维护编排、自动恢复、健康验证和熔断状态 |
| `zh-CN/catalog-management.webp` | 1920×1080 | 物品与帕鲁目录 | 离线目录、图标、分类、别名、收藏和导入 |
| `zh-CN/player-discipline.webp` | 1920×1080 | 玩家纪律 | 白名单、封禁、身份、违规、踢出与审计 |
| `zh-CN/paldefender-console.webp` | 1920×1080 | PalDefender 防护组件 | 连接、版本、配置文件、字段说明与原子保存 |
| `zh-CN/plugin-management.webp` | 1920×1080 | 插件与模组 | 版本、依赖、兼容性、升级、备份和回滚 |
| `zh-CN/user-management.webp` | 1920×1080 | 权限管理 | 多角色账户、启停状态和最近登录 |
| `zh-CN/audit-log.webp` | 1920×1080 | 审计日志 | 关键操作、结果、来源地址和结构化详情 |
| `zh-CN/save-backups.webp` | 1920×1080 | 存档备份 | 备份统计、校验、下载、恢复和删除 |
| `zh-CN/save-diff.webp` | 1920×1080 | 存档差异 | 快照比较、变化分类和异常提示 |
| `zh-CN/save-index.webp` | 1920×1080 | 存档解析 | 快照状态、自动解析、格式检测和手动任务 |
| `zh-CN/notification-channels.webp` | 1920×1080 | 消息通知 | 多渠道 Webhook、事件订阅、模板与重试 |
| `zh-CN/notification-history.webp` | 1920×1080 | 推送记录 | 投递状态、HTTP 结果、耗时和失败原因 |
| `zh-CN/system-logs.webp` | 1920×1080 | 系统日志 | 业务日志、级别筛选和异常定位 |
| `zh-CN/system-settings.webp` | 1920×1080 | 系统设置 | 连接、存档、备份、自动任务和安全配置 |
| `zh-CN/about-project.webp` | 1920×1080 | 关于系统 | 版本、参考项目、数据来源和开源声明 |

## 英文截图清单

英文目录包含与上表一一对应的 27 张 `en-US/*.webp` 图片；英文 README 不得引用 `zh-CN/`。

## 生成与验收规则

- 浏览器视口固定为 **1920×1080**，浅色主题，侧边栏展开；
- 页面必须来自当前可构建的 Vue 代码，API 可由本地拦截器返回伪数据；
- 世界地图必须等待 MapLibre、离线瓦片、服务器标记和静态 POI 渲染完成；
- 中文图应使用 `zh-CN` UI，英文图应使用 `en-US` UI；
- 物资发放必须提供选择玩家、选择资源、核对清单三张独立截图；
- 所有图片不得包含真实服务器、玩家、账号、Token、Webhook、Cookie 或本机用户目录。

## 允许的演示值

可使用 `192.0.2.0/24`、`198.51.100.0/24`、`example.invalid`、虚构玩家（如 Aster、Birch）、虚构公会和 `C:\PalOpsDemo\...` 路径。模糊处理不能替代从数据源删除敏感值。

## 技术徽章与辅助资源

`badges/` 保存 README 顶部本地 SVG 徽章；`map-icon-catalog.png` 是地图分类图标目录。徽章和图标目录不是产品界面截图。
