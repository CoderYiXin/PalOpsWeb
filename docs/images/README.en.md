# Documentation Images and Bilingual Product Screenshots

> Language: [简体中文](README.md) | **English**

`zh-CN/` contains the UI used by the Chinese README and `en-US/` contains the UI used by the English README. Both sets are rendered from the same 1.2.0 Vue build with identical business scenarios, locale-specific fabricated data, and reserved network values. Promotional posters, composite cover art, and cross-locale substitutions are not accepted.

## English screenshot inventory

| File | Size | Module | Public purpose |
|---|---:|---|---|
| `en-US/overview-dashboard.webp` | 1920×1080 | Runtime overview | PalServer, host metrics, versions, saves, and live incidents |
| `en-US/server-statistics.webp` | 1920×1080 | Server statistics | Player, FPS, resource, and operations trends |
| `en-US/player-management.webp` | 1920×1080 | Player archive | Live/indexed players, attributes, inventories, and Pal profiles |
| `en-US/guild-bases.webp` | 1920×1080 | Guilds & bases | Membership, ownership evidence, base details, and map navigation |
| `en-US/world-map.webp` | 1920×1080 | World map | Offline maps, Fast Travel, live players, bases, and custom markers |
| `en-US/resource-grant-step-1.webp` | 1920×1080 | Resource grants · Select players | Target multiple players by identity, guild, and online state |
| `en-US/resource-grant-step-2.webp` | 1920×1080 | Resource grants · Select resources | Categories, multi-term search, item/Pal selection, and bulk add |
| `en-US/resource-grant-step-3.webp` | 1920×1080 | Resource grants · Review | Confirm targets, resources, quantities, and execution scope |
| `en-US/message-center.webp` | 1920×1080 | Messages | Broadcasts, alerts, direct messages, and player actions |
| `en-US/rcon-console.webp` | 1920×1080 | RCON console | Command presets, risk acknowledgement, capability probing, and response history |
| `en-US/palworld-configuration.webp` | 1920×1080 | Palworld Configuration Center | Structured settings, launch arguments, validation, and safe persistence |
| `en-US/automation-jobs.webp` | 1920×1080 | Automation | Schedules, risk levels, next execution, and run history |
| `en-US/maintenance-center.webp` | 1920×1080 | Maintenance & Crash Guard | Maintenance orchestration, recovery, health verification, and circuit state |
| `en-US/catalog-management.webp` | 1920×1080 | Item & Pal catalog | Offline catalog, icons, categories, aliases, favorites, and imports |
| `en-US/player-discipline.webp` | 1920×1080 | Player discipline | Whitelist, bans, identity history, violations, kicks, and audit |
| `en-US/paldefender-console.webp` | 1920×1080 | PalDefender | Connectivity, versions, config files, field help, and atomic persistence |
| `en-US/plugin-management.webp` | 1920×1080 | Plugins & mods | Versions, dependencies, compatibility, updates, backups, and rollback |
| `en-US/user-management.webp` | 1920×1080 | Users & roles | Role-based accounts, status, and recent sign-ins |
| `en-US/audit-log.webp` | 1920×1080 | Audit log | Sensitive actions, outcomes, source addresses, and structured details |
| `en-US/save-backups.webp` | 1920×1080 | Backups | Backup statistics, verification, download, restore, and deletion |
| `en-US/save-diff.webp` | 1920×1080 | Save differences | Snapshot comparison, categorized changes, and anomaly alerts |
| `en-US/save-index.webp` | 1920×1080 | Save Index | Snapshot status, automatic parsing, format detection, and manual jobs |
| `en-US/notification-channels.webp` | 1920×1080 | Notification channels | Multi-provider webhooks, subscriptions, templates, and retries |
| `en-US/notification-history.webp` | 1920×1080 | Delivery history | Delivery status, HTTP results, duration, and failure details |
| `en-US/system-logs.webp` | 1920×1080 | System logs | Operational logs, level filtering, and exception investigation |
| `en-US/system-settings.webp` | 1920×1080 | System settings | Connections, saves, backups, automation, and security configuration |
| `en-US/about-project.webp` | 1920×1080 | About | Version, references, data provenance, and open-source notices |

## Chinese screenshot inventory

The Chinese directory contains the matching 27 `zh-CN/*.webp` images. The Chinese README must not reference `en-US/`.

## Generation and acceptance rules

- Use a **1920×1080** viewport, light theme, and expanded sidebar.
- Render the current buildable Vue code; APIs may be fulfilled by a local fabricated-data interceptor.
- Wait for MapLibre, offline raster tiles, runtime markers, and static POIs before capturing the world map.
- Chinese images use `zh-CN`; English images use `en-US`.
- Resource grants require separate select-player, select-resource, and review screenshots.
- Images must not contain real servers, players, accounts, tokens, webhooks, cookies, or local user paths.

## Approved demonstration values

Reserved ranges `192.0.2.0/24` and `198.51.100.0/24`, `example.invalid`, fictional players such as Aster and Birch, fictional guilds, and `C:\PalOpsDemo\...` are acceptable. Blurring is not a substitute for removing the secret from the data source.

## Technology badges and supporting assets

`badges/` stores the local SVG badges used by the READMEs. `map-icon-catalog.png` documents map category icons. Neither is a product UI screenshot.
