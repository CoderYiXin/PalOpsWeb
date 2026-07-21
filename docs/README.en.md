> Language: [简体中文](README.md) | **English**

# PalOps Web Documentation

This directory contains only the operator, developer, architecture, data, and release documentation required for the current 1.2.0 open-source publication. Historical design drafts, one-off defect reports, internal plans, and packaging audits are excluded.

## Product and operations

| Document | Scope | 中文 |
|---|---|---|
| [Complete feature reference](features.en.md) | Every 1.2.0 module, role, data source, and security boundary | [功能参考](features.md) |
| [Deployment](deployment.en.md) | Windows deployment, upgrade, restore, reverse proxy, and acceptance | [部署说明](deployment.md) |
| [PalDefender deployment](paldefender-deployment.en.md) | Installation, REST, token, firewall, upgrade, and rollback | [PalDefender 部署](paldefender-deployment.md) |
| [PalDefender configuration](paldefender-configuration-management.en.md) | Supported files, validation, backup, concurrency, and reload behavior | [配置管理](paldefender-configuration-management.md) |
| [Release checklist](release-checklist.en.md) | Source, build, runtime, map, screenshot, and archive acceptance | [发布检查](release-checklist.md) |

## Development and architecture

| Document | Scope | 中文 |
|---|---|---|
| [Architecture](architecture.en.md) | Boundaries, authentication, runtime control, saves, maps, persistence, and observability | [系统架构](architecture.md) |
| [Build guide](build.en.md) | Toolchain, canonical build, GitHub Actions, map data, and publication | [构建说明](build.md) |
| [Frontend guide](../frontend-vue/README.en.md) | Vue structure, UI conventions, API contracts, map lifecycle, and verification | [前端开发](../frontend-vue/README.md) |

## Data and public assets

| Document | Scope | 中文 |
|---|---|---|
| [Frontend map bundle](map-data-package.en.md) | Offline frontend map resources, layout, caching, and updates | [前端地图数据包](map-data-package.md) |
| [World map 1.2.0 baseline](world-map-data-1.2.0.en.md) | 1,251 POIs, Fast Travel coverage, refresh, and rendering strategy | [世界地图基线](world-map-data-1.2.0.md) |
| [Paldeck data sources](paldeck-data-sources.en.md) | Offline item/Pal catalog sources, placeholders, and synchronization | [Paldeck 数据来源](paldeck-data-sources.md) |
| [Documentation images](images/README.en.md) | Locale screenshot trees, dimensions, fabricated data, and sanitization | [文档图片规范](images/README.md) |
| [Third-party notices](../THIRD-PARTY-NOTICES.md) | Dependencies, assets, licenses, and attribution | — |

## Repository policies

- [Contributing](../CONTRIBUTING.md)
- [Security policy](../SECURITY.md)
- [Changelog](../CHANGELOG.md)
