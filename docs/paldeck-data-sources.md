# Paldeck 离线目录数据说明

> 语言：**简体中文** | [English](paldeck-data-sources.en.md)

## 数据来源

PalDefender 官方命令文档将以下页面列为 ID 查询来源：

- 帕鲁 ID：`https://paldeck.cc/pals`
- 物品 ID：`https://paldeck.cc/items`
- 科技 ID：`https://paldeck.cc/technology`
- 建筑 ID：`https://paldeck.cc/buildings`
- 被动 ID：`https://paldeck.cc/passives`
- 技能 ID：`https://paldeck.cc/skills`

Paldeck 是非官方社区数据库。PalOps Web 仅在维护目录时同步，生产运行时不向 Paldeck 发起请求。

## 当前内置目录

- 物品：`src/PalOps.Web/Seed/items.json`
- 帕鲁：`src/PalOps.Web/Seed/pals.json`
- 物品静态图：`src/PalOps.Web/StaticCatalog/items`
- 帕鲁静态图：`src/PalOps.Web/StaticCatalog/pals`
- 目录质量报告：`docs/paldeck-catalog-audit.json`

内置目录当前包含 2455 个物品 ID 和 1156 个帕鲁/角色 ID。普通游戏资源优先使用已有中文本地化；首领、突袭、塔主、任务场景、人类 NPC、人类首领、特殊单位与开发测试变体被拆分到独立分类，避免混入普通帕鲁。

当前源码包保留 893 个物品静态图文件和 587 个帕鲁静态图文件。多个品质或场景变体会复用同一张图片；尚未取得独立图片的 1091 个物品条目和 374 个帕鲁/角色条目明确使用本地占位图，不会伪装成已从 Paldeck 下载。具体清单见 `docs/paldeck-catalog-audit.json` 的 `placeholderImageEntries`。

开发、测试、NPC 专用或尚未正式本地化的内部资源采用明确的描述性名称，例如“开发测试：……”或“NPC/伙伴武器：……”。这些名称用于管理后台识别，不宣称是官方译名。

## 六类同步工具

目录维护工具位于 `tools/paldeck-sync`，支持：

- 加载无限滚动列表；
- 访问帕鲁、物品、建筑详情页提取真实内部 ID；
- 下载页面公开静态图；
- 复用本项目已审核的物品/帕鲁中文名称；
- 对科技、建筑、被动和技能应用人工覆盖及术语表；
- 输出缺失 ID、重复 ID、未翻译名称和图片失败清单。

同步输出不会自动覆盖生产目录，必须先人工审核。同步器的 Playwright 依赖固定为已修复浏览器下载证书校验问题的版本，`npm audit --audit-level=high` 应保持零高危漏洞。

## 更新限制

本次构建环境无法解析外部站点 DNS，因此不能在容器内完整执行 Playwright 六站抓取和图片下载。源码包已包含可运行的同步器、中文覆盖文件和离线物品/帕鲁静态资源；在可访问互联网的 Windows 环境运行 `tools/paldeck-sync` 后即可生成六类最新快照。