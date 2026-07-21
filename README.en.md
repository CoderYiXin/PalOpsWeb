<div align="center">

# PalOps Web

### An all-in-one Palworld server operations console built around the PalDefender anti-cheat plugin ecosystem

**1.2.0** brings PalServer lifecycle control, live monitoring, offline world maps, player and guild data, save indexing, configuration, maintenance, discipline, plugins/mods, notifications, and audit into one modern web workbench.

[简体中文](README.md) · [Feature reference](docs/features.en.md) · [English Docs](docs/README.en.md) · [中文文档](docs/README.md)

<a href="https://dotnet.microsoft.com/"><img src="docs/images/badges/dotnet-10.svg" alt=".NET 10" height="28"></a>
<a href="https://vuejs.org/"><img src="docs/images/badges/vue-3.svg" alt="Vue 3" height="28"></a>
<a href="https://github.com/Ultimeit/PalDefender"><img src="docs/images/badges/paldefender-integrated.svg" alt="PalDefender" height="28"></a>
<a href="https://maplibre.org/maplibre-gl-js/docs/"><img src="docs/images/badges/maplibre-offline-map.svg" alt="MapLibre GL JS" height="28"></a>
<a href="https://learn.microsoft.com/en-us/windows-server/"><img src="docs/images/badges/windows-server.svg" alt="Windows Server" height="28"></a>
<a href="LICENSE"><img src="docs/images/badges/license-gpl3.svg" alt="GPL-3.0-or-later" height="28"></a>

</div>

![PalOps Web 1.2.0 Runtime Overview](docs/images/en-US/overview-dashboard.webp)

## What is new in 1.2.0

- **Live world-map layer**: runtime player markers stay above fixed POIs; player refresh supports 1/2/3/5/10/15/30 seconds with a 3-second default, while guild bases and custom markers remain on an independent 30-second poll.
- **Fast Travel coverage update**: nine verified Palworld 1.0 records were added. The offline bundle now contains 94 Fast Travel markers and reports the gap against 149 currently known upstream records.
- **Seven domain navigation groups**: server intelligence, operations, configuration/maintenance, player security, saves/data, extensions, and system governance.
- **Palworld Configuration Center**: structured `PalWorldSettings.ini` and launch-argument editing with syntax/range/conflict validation, diff preview, pre-change backup, atomic replacement, and safe restart.
- **Continuous operations modules**: server statistics, Maintenance & Crash Guard, Save Differences, Player Discipline, and Plugin & Mod Management.
- **PalDefender workflow hardening**: whitelist changes use `whitelist_add` / `whitelist_remove` first and verify `WhiteList.json`; strict UserId validation, persistent kick history, and recently disconnected identity retention were added.

## Why PalOps Web

PalOps Web is not a generic cloud panel. It targets **Windows Palworld Dedicated Servers deployed on the same host as PalOps**, consolidating process control, player data, maps, saves, PalDefender configuration, notifications, and audit.

- **All-in-one operations** across monitoring, configuration, maintenance, security, saves, extensions, and governance.
- **Local-first data** with read-only save parsing, offline map tiles/POIs, local catalogs, and local audit storage.
- **Safe mutations** with identity checks, RBAC, CSRF, SHA-256 concurrency checks, pre-change backups, atomic replacement, and structured audit.
- **Deep PalDefender integration** across REST, extended RCON, whitelist, bans, versions, and configuration files.
- **Verifiable open-source publication** with bilingual READMEs, locale-matched screenshots, documentation contracts, and GitHub Actions.

> PalDefender and PalOps Web are independent projects. Without PalDefender, process control, save indexing, maps, backups, and selected native REST/RCON features remain available; PalDefender-dependent live, protection, and extended operations are clearly reported as unconfigured.

## Complete feature set

| Group | Module | Core capabilities |
|---|---|---|
| Server intelligence | **Runtime overview** | PalServer process, host resources, online players, Server FPS, version center, saves, and live incidents |
| Server intelligence | **Server statistics** | Hourly/daily trends for players, FPS, CPU, memory, incidents, backups, webhooks, and retention |
| Server data | **Player archive** | Live and indexed players, levels, guilds, positions, inventories, Pals, identity sources, and last-seen data |
| Server data | **Guilds & bases** | Members, leaders, ownership evidence, coordinates, workers, and map navigation |
| Server data | **World map** | Offline Palpagos / World Tree maps, 1,251 POIs, live players, bases, custom markers, Fast Travel, and 1/2/3/5/10/15/30-second player refresh |
| Operations | **Resource grants** | Multi-player targeting, localized/internal-ID search, item and Pal carts, experience, and technology points |
| Operations | **Messages** | Broadcasts, alerts, direct messages, online-player selection, kicks, and PalDefender reload |
| Operations | **RCON console** | Standard and PalDefender commands, capability probing, high-risk acknowledgement, output, and history |
| Configuration & maintenance | **Palworld Configuration Center** | Structured PalWorldSettings.ini and launch-argument editing with validation, diff, backup, and safe restart |
| Configuration & maintenance | **Automation** | Cron/interval jobs for backup, broadcast, save, restart, risk classification, and run history |
| Configuration & maintenance | **Maintenance & Crash Guard** | Announcements, save, backup, stop, script, start, health verification, recovery, and circuit breaking |
| Data & saves | **Item & Pal catalog** | Offline catalog, icons, categories, aliases, favorites, imports, and search |
| Player security | **Player discipline** | Whitelist, bans, online players, identity history, violations, kick history, and operation audit |
| Player security | **PalDefender** | Version, connectivity, config generation, field help, validation, backup, atomic writes, and reload guidance |
| Governance | **Users & roles** | Owner, Administrator, Operator, Auditor, and Viewer accounts with enable/disable controls |
| Governance | **Audit log** | Structured audit events for authentication, configuration, lifecycle, RCON, saves, discipline, and access control |
| Data & saves | **Backups** | Manual/automatic backups, SHA-256, retention, download, restore preflight, and protected restore |
| Data & saves | **Save differences** | Read-only comparison of parsed snapshots across players, guilds, bases, items, Pals, and anomalies |
| Data & saves | **Save Index** | Read-only Level.sav / Players snapshots, automatic parsing, format detection, progress, and last-good fallback |
| Extensions | **Plugin & Mod Management** | Version, hash, dependency, compatibility, backup, and rollback management for PalDefender, UE4SS, mods, and scripts |
| Notifications | **Notification channels** | WeCom, DingTalk, Feishu, Discord, Slack, Telegram, and generic JSON webhooks |
| Notifications | **Delivery history** | Success, retry, failure, HTTP status, duration, request/response summaries, and error details |
| System | **System logs** | Operational logs, level filters, full-text search, exception details, and paging |
| System | **System settings** | Palworld REST, PalDefender REST, RCON, save, backup, automation, and security settings |
| System | **About** | Version, dependencies, map provenance, licensing, support boundaries, and release metadata |

See the [complete feature reference](docs/features.en.md) for behavior, permissions, and data sources.

## Product screenshots

### Runtime intelligence and history

<table>
<tr>
<td width="50%"><img src="docs/images/en-US/overview-dashboard.webp" alt="Runtime overview"><br><sub><b>Runtime overview</b> — PalServer, host metrics, versions, saves, and live incidents</sub></td>
<td width="50%"><img src="docs/images/en-US/server-statistics.webp" alt="Server statistics"><br><sub><b>Server statistics</b> — Player, FPS, resource, and operations trends</sub></td>
</tr>
</table>

### Players, guilds, and world map

<table>
<tr>
<td width="50%"><img src="docs/images/en-US/player-management.webp" alt="Player archive"><br><sub><b>Player archive</b> — Live/indexed players, attributes, inventories, and Pal profiles</sub></td>
<td width="50%"><img src="docs/images/en-US/guild-bases.webp" alt="Guilds & bases"><br><sub><b>Guilds & bases</b> — Membership, ownership evidence, base details, and map navigation</sub></td>
</tr>
</table>

<table>
<tr>
<td ><img src="docs/images/en-US/world-map.webp" alt="World map"><br><sub><b>World map</b> — Offline maps, Fast Travel, live players, bases, and custom markers</sub></td>
</tr>
</table>

### Three-step resource grant workflow

<table>
<tr>
<td ><img src="docs/images/en-US/resource-grant-step-1.webp" alt="Resource grants · Select players"><br><sub><b>Resource grants · Select players</b> — Target multiple players by identity, guild, and online state</sub></td>
</tr>
</table>

<table>
<tr>
<td ><img src="docs/images/en-US/resource-grant-step-2.webp" alt="Resource grants · Select resources"><br><sub><b>Resource grants · Select resources</b> — Categories, multi-term search, item/Pal selection, and bulk add</sub></td>
</tr>
</table>

<table>
<tr>
<td ><img src="docs/images/en-US/resource-grant-step-3.webp" alt="Resource grants · Review"><br><sub><b>Resource grants · Review</b> — Confirm targets, resources, quantities, and execution scope</sub></td>
</tr>
</table>

### Daily operations, configuration, and maintenance

<table>
<tr>
<td width="50%"><img src="docs/images/en-US/message-center.webp" alt="Messages"><br><sub><b>Messages</b> — Broadcasts, alerts, direct messages, and player actions</sub></td>
<td width="50%"><img src="docs/images/en-US/rcon-console.webp" alt="RCON console"><br><sub><b>RCON console</b> — Command presets, risk acknowledgement, capability probing, and response history</sub></td>
</tr>
</table>

<table>
<tr>
<td width="50%"><img src="docs/images/en-US/palworld-configuration.webp" alt="Palworld Configuration Center"><br><sub><b>Palworld Configuration Center</b> — Structured settings, launch arguments, validation, and safe persistence</sub></td>
<td width="50%"><img src="docs/images/en-US/automation-jobs.webp" alt="Automation"><br><sub><b>Automation</b> — Schedules, risk levels, next execution, and run history</sub></td>
</tr>
</table>

<table>
<tr>
<td width="50%"><img src="docs/images/en-US/maintenance-center.webp" alt="Maintenance & Crash Guard"><br><sub><b>Maintenance & Crash Guard</b> — Maintenance orchestration, recovery, health verification, and circuit state</sub></td>
<td width="50%"><img src="docs/images/en-US/catalog-management.webp" alt="Item & Pal catalog"><br><sub><b>Item & Pal catalog</b> — Offline catalog, icons, categories, aliases, favorites, and imports</sub></td>
</tr>
</table>

### Player security, PalDefender, and extensions

<table>
<tr>
<td width="50%"><img src="docs/images/en-US/player-discipline.webp" alt="Player discipline"><br><sub><b>Player discipline</b> — Whitelist, bans, identity history, violations, kicks, and audit</sub></td>
<td width="50%"><img src="docs/images/en-US/paldefender-console.webp" alt="PalDefender"><br><sub><b>PalDefender</b> — Connectivity, versions, config files, field help, and atomic persistence</sub></td>
</tr>
</table>

<table>
<tr>
<td width="50%"><img src="docs/images/en-US/plugin-management.webp" alt="Plugins & mods"><br><sub><b>Plugins & mods</b> — Versions, dependencies, compatibility, updates, backups, and rollback</sub></td>
<td width="50%"><img src="docs/images/en-US/user-management.webp" alt="Users & roles"><br><sub><b>Users & roles</b> — Role-based accounts, status, and recent sign-ins</sub></td>
</tr>
</table>

### Saves and change tracking

<table>
<tr>
<td width="50%"><img src="docs/images/en-US/save-backups.webp" alt="Backups"><br><sub><b>Backups</b> — Backup statistics, verification, download, restore, and deletion</sub></td>
<td width="50%"><img src="docs/images/en-US/save-diff.webp" alt="Save differences"><br><sub><b>Save differences</b> — Snapshot comparison, categorized changes, and anomaly alerts</sub></td>
</tr>
</table>

<table>
<tr>
<td width="50%"><img src="docs/images/en-US/save-index.webp" alt="Save Index"><br><sub><b>Save Index</b> — Snapshot status, automatic parsing, format detection, and manual jobs</sub></td>
<td width="50%"><img src="docs/images/en-US/audit-log.webp" alt="Audit log"><br><sub><b>Audit log</b> — Sensitive actions, outcomes, source addresses, and structured details</sub></td>
</tr>
</table>

### Notifications and system governance

<table>
<tr>
<td width="50%"><img src="docs/images/en-US/notification-channels.webp" alt="Notification channels"><br><sub><b>Notification channels</b> — Multi-provider webhooks, subscriptions, templates, and retries</sub></td>
<td width="50%"><img src="docs/images/en-US/notification-history.webp" alt="Delivery history"><br><sub><b>Delivery history</b> — Delivery status, HTTP results, duration, and failure details</sub></td>
</tr>
</table>

<table>
<tr>
<td width="50%"><img src="docs/images/en-US/system-logs.webp" alt="System logs"><br><sub><b>System logs</b> — Operational logs, level filtering, and exception investigation</sub></td>
<td width="50%"><img src="docs/images/en-US/system-settings.webp" alt="System settings"><br><sub><b>System settings</b> — Connections, saves, backups, automation, and security configuration</sub></td>
</tr>
</table>

<table>
<tr>
<td ><img src="docs/images/en-US/about-project.webp" alt="About"><br><sub><b>About</b> — Version, references, data provenance, and open-source notices</sub></td>
</tr>
</table>

## PalDefender integration

```mermaid
flowchart LR
    Admin[Administrator] --> PalOps[PalOps Web]
    PalOps -->|REST Bearer token| PDREST[PalDefender REST]
    PalOps -->|Standard and extended commands| RCON[PalDefender RCON]
    PalOps -->|Validate / backup / atomic replace| Config[PalDefender JSON files]
    PDREST --> Runtime[Players / metrics / version / protection data]
    RCON --> Operations[Whitelist / bans / broadcasts / reloadcfg]
    Config --> Protection[Anti-cheat / logging / administration / gameplay rules]
```

PalOps covers PalDefender version comparison, REST token connectivity, config generation, localized metadata for 76 known fields, JSON/type/path validation, SHA-256 conflict checks, automatic backup, atomic persistence, whitelist/bans, extended RCON probing, and reload/restart guidance.

See [PalDefender deployment](docs/paldefender-deployment.en.md) and [PalDefender configuration management](docs/paldefender-configuration-management.en.md).

## Architecture

```mermaid
flowchart LR
    Browser[Browser
Vue 3 + Element Plus + MapLibre] -->|Cookie + CSRF| Web[PalOps.Web
ASP.NET Core .NET 10]
    Web -->|SignalR| Browser
    Web --> Runtime[PalServer / Shipping
Windows process control]
    Web --> Save[Level.sav / Players
read-only snapshots]
    Web --> PalREST[Palworld REST]
    Web --> RCON[Palworld / PalDefender RCON]
    Web --> Defender[PalDefender REST and config directory]
    Web --> Notify[Webhook channels]
    Web --> Data[(Local JSON / JSONL
settings, backups, audit, statistics)]
    Browser --> Map[Frontend offline map
tiles, POIs, exploration state]
```

Security boundaries:

- The browser never receives Palworld, RCON, or PalDefender credentials.
- Mutating operations pass authentication, RBAC, CSRF, and audit checks.
- Lifecycle operations revalidate the PID, executable, and installation root.
- Save parsing reads private snapshots and retains the last successful index after failures.
- PalDefender and Palworld config writes are path-constrained and use backup, temporary files, write-through, and atomic replacement.
- Fixed map data is frontend-only; the backend supplies only runtime players, bases, and custom markers.

## Quick start

### Requirements

- Windows 10/11 or Windows Server;
- .NET 10 SDK;
- Node.js 22;
- a local Palworld Dedicated Server;
- PalDefender is recommended for the complete live, protection, and extension feature set.

### Clone and build

```powershell
git clone https://github.com/CoderYiXin/PalOpsWeb.git
cd PalOpsWeb
.\scripts\build.ps1
```

### Create a Windows x64 release

```powershell
.\scripts\fetch-map-tiles.ps1 -Layer all
.\scripts\publish-win-x64.ps1 -Version 1.2.0
```

On first launch, initialize the Owner account, configure Palworld REST, RCON, PalDefender REST, saves, and backups, then verify players, maps, configuration, maintenance, and notification modules. Expose the console only through a trusted LAN, VPN, or correctly configured HTTPS reverse proxy.

See the [build guide](docs/build.en.md) and [deployment guide](docs/deployment.en.md).

## Documentation

| Topic | 中文 | English |
|---|---|---|
| Documentation index | [docs/README.md](docs/README.md) | [docs/README.en.md](docs/README.en.md) |
| Complete feature reference | [features.md](docs/features.md) | [features.en.md](docs/features.en.md) |
| Architecture | [architecture.md](docs/architecture.md) | [architecture.en.md](docs/architecture.en.md) |
| Build | [build.md](docs/build.md) | [build.en.md](docs/build.en.md) |
| Deployment | [deployment.md](docs/deployment.md) | [deployment.en.md](docs/deployment.en.md) |
| PalDefender deployment | [paldefender-deployment.md](docs/paldefender-deployment.md) | [paldefender-deployment.en.md](docs/paldefender-deployment.en.md) |
| PalDefender configuration | [paldefender-configuration-management.md](docs/paldefender-configuration-management.md) | [paldefender-configuration-management.en.md](docs/paldefender-configuration-management.en.md) |
| World map data 1.2.0 | [world-map-data-1.2.0.md](docs/world-map-data-1.2.0.md) | [world-map-data-1.2.0.en.md](docs/world-map-data-1.2.0.en.md) |
| Screenshot policy | [images/README.md](docs/images/README.md) | [images/README.en.md](docs/images/README.en.md) |
| Release checklist | [release-checklist.md](docs/release-checklist.md) | [release-checklist.en.md](docs/release-checklist.en.md) |

## GitHub Actions

`.github/workflows/build.yml` runs frontend contracts, TypeScript, Vite, npm high-severity auditing, .NET 10 restore/build, catalog/map/document/source verification, and repository hygiene checks for runtime data, secrets, Python source, Japanese Markdown, and build caches.

## Security, contributing, and license

Do not expose PalOps, Palworld REST, PalDefender REST, or RCON directly to the public Internet. Never commit `data`, saves, logs, databases, Data Protection keys, passwords, tokens, or cookies.

See [SECURITY.md](SECURITY.md), [CONTRIBUTING.md](CONTRIBUTING.md), and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

PalOps Web is licensed under **GNU GPL v3 or later**. Palworld names, trademarks, and game assets belong to their respective owners. This project is not affiliated with or endorsed by Pocketpair.
