> 语言：**简体中文** | [English](README.en.md)

# PalOps Web 文档

本目录只保留当前 1.3.1 开源发布需要的运营、开发、架构、数据和发布文档。历史设计稿、一次性缺陷报告、内部计划和打包审计不进入公开源码。

## 产品与运营

| 文档 | 内容 | English |
|---|---|---|
| [完整功能参考](features.md) | 1.3.1 全部功能模块、角色、数据来源和安全边界 | [Feature Reference](features.en.md) |
| [部署说明](deployment.md) | Windows 部署、升级、恢复、反向代理和上线验证 | [Deployment](deployment.en.md) |
| [PalDefender 部署](paldefender-deployment.md) | 安装、REST、Token、防火墙、升级与回滚 | [PalDefender Deployment](paldefender-deployment.en.md) |
| [PalDefender 配置管理](paldefender-configuration-management.md) | 支持文件、校验、备份、并发控制与重载行为 | [Configuration Management](paldefender-configuration-management.en.md) |
| [发布检查](release-checklist.md) | 源码、构建、运行、地图、截图与压缩包验收 | [Release Checklist](release-checklist.en.md) |

## 开发与架构

| 文档 | 内容 | English |
|---|---|---|
| [系统架构](architecture.md) | 应用边界、认证、运行控制、存档、地图、持久化与可观测性 | [Architecture](architecture.en.md) |
| [构建说明](build.md) | 工具链、标准构建命令、GitHub Actions、地图数据和发布 | [Build Guide](build.en.md) |
| [前端开发](../frontend-vue/README.md) | Vue 目录、UI 约定、API 契约、地图生命周期和验证命令 | [Frontend Guide](../frontend-vue/README.en.md) |

## 数据与公开资源

| 文档 | 内容 | English |
|---|---|---|
| [前端地图数据包](map-data-package.md) | 前端离线地图资源、目录结构、缓存与更新 | [Map Data Package](map-data-package.en.md) |
| [世界地图 1.2.0 基线](world-map-data-1.2.0.md) | 1,251 条 POI、快速传送覆盖、刷新和渲染策略 | [World Map Baseline](world-map-data-1.2.0.en.md) |
| [Paldeck 数据来源](paldeck-data-sources.md) | 离线物品/帕鲁目录来源、占位数据和同步规则 | [Paldeck Data Sources](paldeck-data-sources.en.md) |
| [文档图片规范](images/README.md) | 中英文截图目录、尺寸、伪数据与脱敏要求 | [Documentation Images](images/README.en.md) |
| [第三方声明](../THIRD-PARTY-NOTICES.md) | 依赖、资源、许可证与归属 | — |

## 仓库策略

- [贡献指南](../CONTRIBUTING.md)
- [安全策略](../SECURITY.md)
- [变更记录](../CHANGELOG.md)
