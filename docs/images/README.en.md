# Documentation Images and Bilingual Product Screenshots

> Language: [简体中文](README.md) | **English**

`zh-CN/` and `en-US/` contain the V1.3.0 Chinese and English README screenshots. Both sets use the same synthetic business scenarios and a fixed **1920×1080** viewport.

## English screenshot inventory

| File | Module | Public purpose |
|---|---|---|
| `en-US/overview-dashboard.webp` | Runtime overview | PalServer, host resources, versions, saves, and live events |
| `en-US/player-management.webp` | Player management | Live/indexed players, characters, inventories, and Pal profiles |
| `en-US/guild-bases.webp` | Guilds & bases | Membership, ownership evidence, base details, and map navigation |
| `en-US/world-map.webp` | World map | Offline map, fixed POIs, players, bases, and custom markers |
| `en-US/palworld-configuration.webp` | Palworld configuration | Structured settings, launch arguments, diagnostics, and safe persistence |
| `en-US/resource-grant-step-1.webp` | Resource grants · Step 1 | Select one or more target players |
| `en-US/resource-grant-step-2.webp` | Resource grants · Step 2 | Search and bulk-select items/Pals |
| `en-US/resource-grant-step-3.webp` | Resource grants · Step 3 | Review targets, resources, quantities, and scope |
| `en-US/message-center.webp` | Messages | Broadcasts, warnings, direct messages, and player actions |
| `en-US/rcon-console.webp` | RCON console | Presets, capability probing, risk acknowledgement, and history |
| `en-US/automation-jobs.webp` | Automation | Schedules, risk levels, next execution, and results |
| `en-US/maintenance-center.webp` | Maintenance center | Maintenance orchestration, crash guard, health checks, and recovery |
| `en-US/server-statistics.webp` | Server statistics | Player, FPS, resource, and operations trends |
| `en-US/save-diff.webp` | Save differences | Snapshot changes, categories, and anomaly signals |
| `en-US/player-discipline.webp` | Player discipline | Whitelist, bans, identity, violations, kicks, and audit |
| `en-US/save-backups.webp` | Save backups | Backup, verification, download, restore preflight, and retention |
| `en-US/diagnostic-center.webp` | Diagnostic center | Process, network, files, configuration, resources, and support bundles |
| `en-US/incident-center.webp` | Incident center | Alert rules, acknowledgement, assignment, recovery, and timelines |
| `en-US/player-insights.webp` | Player insights | Player timelines, activity, churn signals, and notes |
| `en-US/world-governance.webp` | World governance | Base ownership, governance candidates, review, and human notes |
| `en-US/disaster-recovery.webp` | Disaster recovery | DR targets, RPO/RTO, validation, and recovery drills |
| `en-US/update-center.webp` | Update center | Component inventory, versions, preflight, approval, and health validation |
| `en-US/configuration-versions.webp` | Configuration versions | Snapshots, diffs, current match, and controlled rollback |
| `en-US/operations-playbooks.webp` | Operations playbooks | Allow-listed actions, steps, run history, and confirmation |
| `en-US/security-center.webp` | Security center | Policy, API tokens, scopes, expiry, and revocation |
| `en-US/integration-center.webp` | External integrations | HTTPS subscriptions, secret references, retries, and delivery history |
| `en-US/notification-channels.webp` | Notification channels | Multi-provider webhooks, subscriptions, templates, and retries |
| `en-US/notification-history.webp` | Delivery history | Status, HTTP results, latency, and failure details |
| `en-US/system-settings.webp` | System settings | First-run tutorial, readiness checklist, connections, saves, and backups |
| `en-US/paldefender-console.webp` | PalDefender | Connectivity, versions, config files, help, and atomic persistence |
| `en-US/plugin-management.webp` | Plugins & mods | Versions, dependencies, compatibility, updates, backups, and rollback |
| `en-US/save-index.webp` | Save Index | Snapshots, automatic parsing, format inspection, and manual jobs |
| `en-US/catalog-management.webp` | Catalog management | Item/Pal catalog, categories, aliases, favorites, and imports |
| `en-US/audit-log.webp` | Audit log | Sensitive actions, outcomes, sources, and structured details |
| `en-US/system-logs.webp` | System logs | Operational logs, level filters, search, and exception investigation |
| `en-US/user-management.webp` | Users & roles | Role-based accounts, state, and recent sign-ins |
| `en-US/about-project.webp` | About | Version, data provenance, dependencies, and license |

## Generation and acceptance rules

- Capture the current V1.3.0 Vue pages; promotional posters and cross-locale substitutions are not accepted.
- APIs and SignalR may be fulfilled by a local synthetic-data interceptor, but demo data must not enter runtime source or release configuration.
- The Chinese README references only `zh-CN/`; the English README references only `en-US/`.
- Resource grants require three complete screenshots: select players, select resources, and review/execute.
- The world map must show the offline basemap, fixed POIs, runtime players, bases, and custom markers.
- Pages must not contain auto-open dialogs, `[object Object]`, error overlays, or unhandled exceptions.
- Images must not contain real IP addresses, world IDs, players, accounts, tokens, webhooks, cookies, Data Protection keys, or local user directories.

## Approved demonstration data

Reserved ranges `192.0.2.0/24` and `198.51.100.0/24`, `example.invalid`, fictional players/guilds, and `C:\PalOpsDemo\...` paths are allowed. Blurring is not a substitute for removing secrets from the data source.

`badges/` stores local SVG technology badges used by the READMEs and is not part of the product screenshot count.
