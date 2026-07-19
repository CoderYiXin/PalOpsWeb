# Documentation Images and Product Screenshots

> Language: [简体中文](README.md) | **English**

This directory contains the product screenshots used by the GitHub README and public documentation. Every screenshot is generated from the current PalOps Web frontend with a sanitized local demonstration profile. Server, account, player, guild, path, coordinate, token, webhook, and operation-result values are fabricated. The map uses the repository's offline tiles and POI data and does not include responses from a private server.

## Module screenshots

| File | Size | Module | Public purpose |
|---|---:|---|---|
| `overview-dashboard.webp` | 1920×1080 | Operations overview | README hero image covering PalServer, host, save, PalDefender, and event status |
| `player-management.webp` | 1920×1080 | Player management | Online/offline players, character attributes, inventories, and Pal profiles |
| `guild-bases.webp` | 1920×1080 | Guild bases | Guild membership, ownership evidence, and map navigation |
| `world-map.webp` | 1920×1080 | World map | Palpagos / World Tree, fixed POIs, players, bases, and custom markers |
| `resource-grant.webp` | 1920×1080 | Resource grants | Multi-target selection, resource search, task cart, and execution result |
| `message-center.webp` | 1920×1080 | Message center | Broadcasts, warnings, direct messages, and player selection |
| `rcon-console.webp` | 1920×1080 | RCON console | Standard/PalDefender commands, risk detection, and response history |
| `automation-jobs.webp` | 1920×1080 | Automation jobs | Schedules, risk levels, next execution, and run history |
| `save-backups.webp` | 1920×1080 | Save backups | Backup creation, SHA-256 verification, download, restore, and deletion |
| `notification-channels.webp` | 1920×1080 | Notification channels | Multi-provider webhooks, subscriptions, templates, and retry policy |
| `notification-history.webp` | 1920×1080 | Delivery history | Delivery status, HTTP result, latency, and failure reason |
| `system-settings.webp` | 1920×1080 | System settings | Palworld, PalDefender, RCON, save, backup, and automation settings |
| `paldefender-console.webp` | 1920×1080 | Protection component | PalDefender connectivity, versions, configuration files, and field help |
| `save-index.webp` | 1920×1080 | Save indexing | Snapshot index, automatic parsing, format detection, and fallback state |
| `catalog-management.webp` | 1920×1080 | Catalog management | Item/Pal catalogs, categories, favorites, and aliases |
| `audit-log.webp` | 1920×1080 | Audit log | Sensitive operations, outcomes, source addresses, and structured details |
| `system-logs.webp` | 1920×1080 | System logs | Noise-reduced business logs, level filters, and exception details |
| `user-management.webp` | 1920×1080 | Access management | Role-based accounts, enable/disable state, and recent sign-in data |
| `about-project.webp` | 1920×1080 | About | Version, referenced projects, data sources, and open-source notices |

## Supporting images

| File | Size | Purpose |
|---|---:|---|
| `overview-light.webp` | 1920×1080 | Additional light-theme view |
| `overview-dark.webp` | 1920×1080 | Dark theme and long-running operations view |
| `webhook-notifications.webp` | 1920×1080 | Legacy notification view retained for documentation compatibility |
| `map-icon-catalog.png` | 1020×756 | Map category icon catalog |

## Sanitization policy

Approved demonstration values include the reserved address `192.0.2.10`, `webhook.example.invalid`, fictional player names such as `Aster` and `Birch`, fictional guilds, and the non-user path `C:\PalOpsDemo\...`.

Before replacing or adding an image, verify that it contains no:

- real IP address, hostname, server port, or public endpoint;
- Windows username, installation path, save path, or backup path;
- player UID, SteamID, real guild name, or private chat content;
- cookie, token, RCON password, or PalDefender key;
- webhook secret, signed URL, QR code, or request header;
- browser bookmark, notification, account avatar, or unrelated personal data.

Blur is not a substitute for removing the underlying secret. Public screenshots must be regenerated with a dedicated demonstration profile and fabricated data.
