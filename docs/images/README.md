# 文档图片与双语产品截图

> 语言：**简体中文** | [English](README.en.md)

`zh-CN/` 和 `en-US/` 分别保存 V1.3.1 中文、英文 README 使用的真实前端页面截图。两套截图使用同一批业务场景和专用伪数据，视口均为 **1920×1080**。 每种语言各 **37 张**，合计 **74 张**产品截图。

## 中文截图清单

| 文件 | 模块 | 公开用途 |
|---|---|---|
| `zh-CN/overview-dashboard.webp` | 运行概览 | PalServer、主机资源、版本、存档与实时事件 |
| `zh-CN/player-management.webp` | 玩家管理 | 在线/存档玩家、角色、背包与帕鲁档案 |
| `zh-CN/guild-bases.webp` | 公会据点 | 成员、归属证据、据点详情与地图定位 |
| `zh-CN/world-map.webp` | 世界地图 | 离线地图、固定 POI、玩家、据点与自定义标记 |
| `zh-CN/palworld-configuration.webp` | Palworld 配置 | 结构化参数、启动参数、诊断与安全保存 |
| `zh-CN/resource-grant-step-1.webp` | 物资发放 · 第一步 | 选择一个或多个在线玩家 |
| `zh-CN/resource-grant-step-2.webp` | 物资发放 · 第二步 | 搜索并批量选择物品/帕鲁 |
| `zh-CN/resource-grant-step-3.webp` | 物资发放 · 第三步 | 核对玩家、资源、数量与执行范围 |
| `zh-CN/message-center.webp` | 消息发送 | 公告、警告、定向消息与玩家操作 |
| `zh-CN/rcon-console.webp` | RCON 控制台 | 命令模板、能力探测、风险确认与历史 |
| `zh-CN/automation-jobs.webp` | 自动任务 | 周期任务、风险等级、下次运行与结果 |
| `zh-CN/maintenance-center.webp` | 维护中心 | 维护编排、崩溃守护、健康验证与恢复 |
| `zh-CN/server-statistics.webp` | 服务器统计 | 玩家、FPS、资源与运营趋势 |
| `zh-CN/save-diff.webp` | 存档差异 | 快照变化、分类与异常提示 |
| `zh-CN/player-discipline.webp` | 玩家纪律 | 白名单、封禁、身份、违规、踢出与审计 |
| `zh-CN/save-backups.webp` | 存档备份 | 备份、校验、下载、恢复预检与保留策略 |
| `zh-CN/diagnostic-center.webp` | 诊断中心 | 进程、网络、文件、配置、资源与支持包 |
| `zh-CN/incident-center.webp` | 事件与故障中心 | 告警规则、确认、分派、恢复与时间线 |
| `zh-CN/player-insights.webp` | 玩家洞察 | 玩家时间线、活跃度、流失信号与备注 |
| `zh-CN/world-governance.webp` | 世界治理 | 据点归属、治理候选、复核与人工说明 |
| `zh-CN/disaster-recovery.webp` | 灾备中心 | 灾备目标、RPO/RTO、验证与恢复演练 |
| `zh-CN/update-center.webp` | 更新中心 | 组件盘点、版本、预检、审批与健康验证 |
| `zh-CN/configuration-versions.webp` | 配置版本库 | 快照、差异、当前匹配与受控回滚 |
| `zh-CN/operations-playbooks.webp` | 运维剧本 | 白名单动作、步骤、执行历史与确认 |
| `zh-CN/security-center.webp` | 安全中心 | 策略、API Token、作用域、有效期与吊销 |
| `zh-CN/integration-center.webp` | 对外集成 | HTTPS 订阅、签名引用、重试与投递历史 |
| `zh-CN/notification-channels.webp` | 消息通知 | 多渠道 Webhook、订阅、模板与重试 |
| `zh-CN/notification-history.webp` | 推送记录 | 状态、HTTP 结果、耗时与失败原因 |
| `zh-CN/system-settings.webp` | 系统设置 | 首次使用教程、就绪清单、连接、存档与备份 |
| `zh-CN/paldefender-console.webp` | PalDefender 防护组件 | 连接、版本、配置文件、说明与原子保存 |
| `zh-CN/plugin-management.webp` | 插件与模组 | 版本、依赖、兼容性、更新、备份与回滚 |
| `zh-CN/save-index.webp` | 存档解析 | 快照、自动解析、格式检测与手动任务 |
| `zh-CN/catalog-management.webp` | 目录管理 | 物品/帕鲁目录、分类、别名、收藏与导入 |
| `zh-CN/audit-log.webp` | 审计日志 | 关键操作、结果、来源与结构化详情 |
| `zh-CN/system-logs.webp` | 系统日志 | 业务日志、级别筛选、搜索与异常定位 |
| `zh-CN/user-management.webp` | 权限管理 | 角色账户、启停状态与最近登录 |
| `zh-CN/about-project.webp` | 关于系统 | 版本、数据来源、依赖与许可证 |

## 截图生成与验收规则

- 截图必须来自当前 V1.3.1 Vue 页面，不得用宣传海报或另一语言界面替代。
- API 和 SignalR 可由本地拦截器提供伪数据，但伪数据不得写入运行时源码或发布配置。
- 中文 README 只引用 `zh-CN/`；英文 README 只引用 `en-US/`。
- 物资发放必须独立展示“选择玩家 → 选择资源 → 核对执行”三张完整截图。
- 世界地图应显示离线底图、固定 POI、服务器玩家、据点和自定义标记。
- 所有页面不得出现自动弹窗、`[object Object]`、错误遮罩或未处理异常。
- 不得包含真实 IP、世界 ID、玩家、账号、Token、Webhook、Cookie、Data Protection 密钥或本机用户目录。

## 允许的演示数据

允许使用 `192.0.2.0/24`、`198.51.100.0/24`、`example.invalid`、虚构玩家/公会及 `C:\PalOpsDemo\...` 路径。模糊处理不能替代从数据源删除秘密。

`badges/` 保存 README 使用的本地 SVG 技术徽章，不计入产品截图数量。
