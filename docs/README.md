# PalOps Web 文档

> 语言：**简体中文** | [English](README.en.md)

本目录只保留面向开源用户、部署人员和贡献者的当前有效文档。历史设计稿、内部实施计划、单次缺陷报告和打包审计记录不进入公开源码仓库。

## 运维与部署

| 文档 | 说明 | English |
|---|---|---|
| [部署指南](deployment.md) | Windows 本机部署、首次启动、升级、回滚、反向代理和数据目录 | [Deployment](deployment.en.md) |
| [PalDefender 部署](paldefender-deployment.md) | 安装、REST、Token、防火墙、升级、回滚和故障排查 | [PalDefender Deployment](paldefender-deployment.en.md) |
| [PalDefender 配置管理](paldefender-configuration-management.md) | 文件白名单、字段说明、校验、备份、并发控制和原子保存 | [Configuration Management](paldefender-configuration-management.en.md) |
| [发布检查清单](release-checklist.md) | 源码、构建、运行、地图、PalDefender 和压缩包验收 | [Release Checklist](release-checklist.en.md) |

## 开发与架构

| 文档 | 说明 | English |
|---|---|---|
| [架构说明](architecture.md) | 应用边界、权限、存档解析、地图、进程控制、通知和持久化 | [Architecture](architecture.en.md) |
| [构建说明](build.md) | 工具链、标准构建命令、GitHub Actions、地图数据和发布 | [Build Guide](build.en.md) |
| [前端开发](../frontend-vue/README.md) | Vue 目录、UI 约定、API 契约、地图生命周期和验证命令 | [Frontend Guide](../frontend-vue/README.en.md) |

## 数据与第三方集成

| 文档 | 说明 | English |
|---|---|---|
| [前端地图数据包](map-data-package.md) | 纯前端地图资源、目录结构、缓存和更新方式 | [Map Data Package](map-data-package.en.md) |
| [世界地图 1.1.0 基线](world-map-data-1.1.0.md) | POI、分类、瓦片、默认显隐和数据更新基线 | [World Map Baseline](world-map-data-1.1.0.en.md) |
| [Paldeck 数据来源](paldeck-data-sources.md) | 离线物品/帕鲁目录来源、占位数据和同步规则 | [Paldeck Data Sources](paldeck-data-sources.en.md) |
| [文档图片规范](images/README.md) | 截图尺寸、演示数据、脱敏和更新要求 | [Documentation Images](images/README.en.md) |
| [第三方声明](../THIRD-PARTY-NOTICES.md) | 依赖、资源、许可证与归属记录 | — |

## 仓库策略

- [贡献指南](../CONTRIBUTING.md)
- [安全策略](../SECURITY.md)
- [变更记录](../CHANGELOG.md)
