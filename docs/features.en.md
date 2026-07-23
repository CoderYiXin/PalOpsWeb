# PalOps Web 1.3.1 Complete Feature Reference

> Language: [简体中文](features.md) | **English**

This document follows the current 1.3.1 routes and server-side authorization boundaries. English screenshots are stored under [`images/en-US/`](images/en-US/) and are rendered from the current Vue pages with fabricated demonstration data.

## Server intelligence

### Runtime overview

A single dashboard for the PalServer process, host CPU/memory/disk, online players, Server FPS, platform and PalDefender versions, save status, and live incidents.

### Server statistics

Hourly or daily trends for players, FPS, CPU, memory, incidents, backups, and webhook activity, including retention coverage and update status.

## Server data

### Player archive

Merges live state with the read-only save index to show level, guild, position, inventory, Pals, identity sources, last-seen time, and match confidence.

### Guilds & bases

Shows members, leaders, ownership evidence, world coordinates, worker Pals, and base details with direct world-map navigation.

### World map

Provides offline Palpagos / World Tree maps, 1,251 fixed POIs, 94 Fast Travel markers, live players, guild bases, and custom markers. Player refresh supports 1/2/3/5/10/15/30 seconds with a 3-second default; bases and custom markers refresh independently every 30 seconds.

## Operations

### Resource grants

A three-step workflow for selecting multiple players, searching localized/internal IDs, building item and Pal carts, and granting experience or technology points.

### Messages

Supports server broadcasts, warnings, direct messages, online-player selection, kicks, and controlled PalDefender configuration reloads.

### RCON console

Provides standard Palworld and PalDefender extended commands, capability probing, command presets, high-risk acknowledgement, output, and history.

## Configuration & maintenance

### Palworld Configuration Center

Structured management of `PalWorldSettings.ini` and launch arguments with raw-text editing, type/range/syntax/port-conflict validation, diff preview, pre-change backup, atomic replacement, and safe restart.

### Automation

Creates Cron or fixed-interval tasks for backups, announcements, saves, and restarts, with risk classification, next-run time, and execution history.

### Maintenance & Crash Guard

Orchestrates announcements, save, backup, stop, scripts, start, and health verification, while exposing crash detection, automatic recovery, backoff, and circuit state.

## Player security

### Player discipline

Manages whitelist, bans, online players, identity associations, violations, and kick history. Whitelist changes prefer PalDefender RCON and verify the resulting file; strict UserId validation blocks incorrect writes.

### PalDefender

Shows connectivity and versions, generates and maintains supported configuration files, and provides field help, JSON/type/path validation, SHA-256 concurrency checks, backups, atomic persistence, and `reloadcfg` / restart guidance.

### Users & roles

Manages Owner, Administrator, Operator, Auditor, and Viewer accounts, status, and recent sign-ins.

### Audit log

Records actor, source, outcome, and structured metadata for authentication, configuration, lifecycle, RCON, saves, player discipline, plugins, and access control.

## Saves & data

### Item & Pal catalog

Uses an offline catalog and icons with categories, aliases, favorites, imports, multi-term search, and data-source attribution.

### Backups

Supports manual/automatic backups, SHA-256 verification, retention policies, download, restore preflight, and protected restore.

### Save differences

Read-only comparison of two parsed snapshots across players, guilds, bases, items, and Pals, with anomaly and high-risk change classification.

### Save Index

Read-only parsing of `Level.sav` and `Players` snapshots with automatic indexing, format detection, progress, diagnostics, and last-good fallback.

## Extensions

### Plugin & Mod Management

Tracks versions, hashes, dependencies, compatibility, installation state, backups, and rollback data for PalDefender, UE4SS, server mods, and scripts.

## Notifications

### Notification channels

Supports WeCom, DingTalk, Feishu, Discord, Slack, Telegram, and generic JSON webhooks with event subscriptions, templates, tests, and retry policies.

### Delivery history

Shows successful, retried, and failed deliveries with HTTP status, duration, request/response summaries, and errors while keeping secrets redacted.

## System

### System logs

Provides operational logs, level filtering, full-text search, exception details, and paging.

### System settings

Centralizes Palworld REST, PalDefender REST, RCON, saves, backups, automation, runtime refresh, and security options.

### About

Shows the PalOps Web version, major dependencies, map-data provenance, licensing, project boundaries, and release metadata.

## Advanced operations and governance

### Diagnostics Center

Runs centralized process, REST, RCON, save, backup, disk, automation, and plugin baseline checks with graded results, remediation guidance, and protected support-bundle export.

### Incident Center

Aggregates health anomalies into incidents that can be acknowledged, assigned, commented, resolved, and reopened. Rules support severity, duplicate aggregation, and an auditable timeline.

### Player Insights

Builds player timelines, activity indicators, and advisory risk signals from live sessions, save indexes, discipline records, and administrator notes. Risk signals never trigger automatic bans.

### World Governance

Analyzes guilds, bases, and activity to identify inactive, orphaned, or review-worthy governance candidates. It never deletes or moves bases automatically.

### Disaster Recovery Center

Manages local, UNC, WebDAV, and S3-compatible target definitions, validation, and recovery drills. A drill copies a verified backup into an isolated directory and never overwrites the production save.

### Update Center

Provides preflight checks, risks, approval records, and plans for PalServer, PalDefender, and plugin components. It currently orchestrates review and approval only; it never silently replaces server binaries.

### Configuration Version Library

Creates immutable snapshots of Palworld configuration and launch arguments with hash verification, key-level differences, and Owner-only exact-confirmation restore.

### Operations Playbooks

Combines health refresh, backup, save indexing, notification events, and maintenance into allowlisted steps. Playbooks cannot execute arbitrary processes, shell commands, or PowerShell.

### Security Center

Manages API-token policy, scopes, expiration, revocation, and usage observations. Plaintext is shown once at creation; persistence stores only the SHA-256 hash.

### Integration Center

Provides scoped Bearer-token APIs and HTTPS event subscriptions. Subscriptions reuse the existing webhook queue, retries, SSRF validation, and delivery history. Inbound events require an `external.*` type and cannot invoke arbitrary RCON or shell actions.

## Roles and mutations

- **Owner**: full access, including accounts, security configuration, restore, plugins/mods, and high-risk lifecycle operations.
- **Administrator**: server operations, players, guilds, configuration, maintenance, notifications, and most system administration.
- **Operator**: daily runtime, messaging, RCON, automation, backup, and authorized player operations.
- **Auditor**: logs, audit, statistics, save differences, and read-only security data.
- **Viewer**: read-only status and basic data.

Server-side authorization policies are authoritative; hiding frontend controls is not an authorization boundary.

## Data sources

| Data | Primary source |
|---|---|
| Process, CPU, memory, disk | Local Windows process and system metrics |
| Online players and Server FPS | Palworld REST / PalDefender REST with explicit fallback |
| Players, guilds, bases, items, and Pals | Read-only local save parsing merged with live data |
| Fixed map POIs, tiles, categories, and exploration state | Offline Vue frontend assets |
| Runtime map players, bases, and custom markers | PalOps backend runtime API |
| Whitelist, bans, and protection configuration | PalDefender RCON, REST, and constrained JSON files |
| Settings, statistics, audit, and job history | Local PalOps JSON / JSONL data |

## Security principles

Mutations use cookie sessions, CSRF, role authorization, and audit. Configuration updates use constrained paths, concurrency hashes, pre-change backups, and atomic replacement. Restore, force-stop, and plugin installation require elevated permissions and confirmation. Save differences and save indexing remain read-only.
