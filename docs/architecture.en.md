> Language: [简体中文](architecture.md) | **English**

# Architecture

## Supported deployment boundary

PalOps Web is a single ASP.NET Core .NET 10 application that hosts one compiled Vue single-page application. The supported deployment model is **Windows-local**: PalOps Web, `PalServer.exe`, the selected world save, and the optional PalDefender endpoint run on the same Windows host.

```text
Desktop browser
    │ authenticated Cookie + CSRF-protected HTTP
    │ authenticated SignalR state/event stream
    ▼
PalOps.Web (.NET 10)
    ├─ Vue static assets under wwwroot
    ├─ Palworld official REST and TCP RCON clients
    ├─ PalDefender REST client and release checker
    ├─ read-only save snapshot/decompression/projection pipeline
    ├─ Windows PalServer discovery and process control
    ├─ metrics, health, backup, automation, audit, and notification services
    └─ versioned local JSON/JSONL persistence
```

Remote process agents, Linux process control, UNC save locations, and direct browser-to-PalServer control are outside the supported boundary. Webhook destinations are outbound integrations only; they do not become trusted control channels.

## Application composition root

`src/PalOps.Web/Program.cs` is the composition root. It configures:

- cookie authentication, CSRF validation, role policies, and endpoint groups;
- singleton stores and infrastructure services for settings, audit, backups, automation, maps, runtime configuration, notifications, and save indexes;
- scoped player, grant, and management services;
- hosted services for file logging, startup diagnostics, save monitoring, automation scheduling, runtime monitoring, metrics delivery, player-presence detection, and Webhook dispatch;
- `PalOpsHub` at `/hubs/palops`;
- the compiled Vue application from `src/PalOps.Web/wwwroot`.

Service boundaries are expressed through interfaces such as `IPalServerRuntimeCoordinator`, `IPalOpsEventBus`, `ISaveIndexRepository`, and `IWebhookChannelStore`. Endpoint classes orchestrate these interfaces instead of performing filesystem, process, RCON, or outbound HTTP work directly.

## Authentication, authorization, and audit

Authentication uses the local account store, password hashes, cookie claims, and the existing CSRF flow. Backend policies are authoritative; navigation visibility is only a presentation aid.

The supported roles are:

- **Owner** — all operations, including runtime configuration, Webhook secrets, and force-stop;
- **Administrator** — normal server lifecycle operations and routine notification administration;
- **Operator** — approved operational actions without sensitive configuration or force-stop access;
- **Auditor** — read-only operational, delivery, and audit visibility;
- **Viewer** — basic read-only visibility retained for compatibility.

Mutations record the authenticated actor, operation, result, reason where applicable, and traceable timestamps. Sensitive values are excluded or redacted before audit and system-log persistence.

## Save ingestion and projection

The save pipeline is read-only with respect to the Palworld source files:

```text
Resolve and guard world path
    → wait for stable Level.sav and Players directory
    → copy a private snapshot
    → calculate source hashes
    → unwrap the SAV envelope
    → decompress PlZ or PlM1
    → parse bounded GVAS properties and supported RawData
    → project player/world/guild/base/container domains
    → reconcile cross-domain identifiers
    → validate the complete snapshot
    → atomically publish the new save index
```

`PlM1` decompression uses the bundled `ooz.wasm` module through Wasmtime. No proprietary Oodle DLL or external parser process is started. Decompression and parsing enforce size, recursion, collection, and boundary limits. A failed parse is recorded but never replaces the last successful index.

Player-save files contribute identity, platform diagnostics, transforms, last-online data, and container references. `Level.sav` contributes character profiles, item/character containers, guilds, and bases. Joins use normalized stable identifiers rather than display names.

## Coordinate placement and map layers

Fixed map assets are a frontend-only boundary. The release tree contains both raster layer descriptors, 682 local tiles, marker icons, category metadata, bounds, coordinate transforms, and three cacheable locale POI JSON files with 1,242 records each. The POI arrays are not synchronously imported into the map route chunk. The backend does not scan tiles, validate a basemap, load fixed POIs, import map packages, or project runtime entities.

Runtime map data follows this path:

```text
PalOps save/live services
    → `/api/v1/map/entities` returns raw world or direct map coordinates
    → `useMapSources.ts` selects the active frontend layer
    → `useMapCoordinates.ts` applies the bundled transform
    → frontend bounds check
    → MapLibre source update
```

Palpagos and World Tree keep separate frontend layer configurations and view state. Source world coordinates remain on runtime entities for diagnostics. Invalid or out-of-bounds coordinates are omitted by the browser instead of being forced outside the canvas. Only players, guild bases, and custom markers are server-owned map entities; fixed POIs and exploration state never depend on a map-data backend.

## Guild/base reconciliation

`GuildBaseReconciliationService` consolidates base ownership and position evidence before the save index is published. Evidence is ranked from direct to inferred:

- direct base guild/group identifiers;
- guild base-ID collections;
- member/owner/worker/build-related player identifiers;
- linked map or base-core objects used to recover missing positions.

Each projected base records an association type, position source, and related player identifiers. Bases are de-duplicated by stable base ID, or by a normalized fallback identity when a base ID is absent. A base with a valid position but unresolved guild remains visible as an unresolved base; it is not discarded.

## PalServer runtime management

Runtime management is implemented under `src/PalOps.Web/ServerRuntime/`. The main boundary is `IPalServerRuntimeCoordinator`, implemented by `PalServerRuntimeCoordinator`.

The subsystem includes:

- `PalServerDiscoveryService` — derives candidate roots, scripts, and `PalServer.exe` from the confirmed save path;
- `PalServerProcessLocator` — identifies the actual game process by recorded PID and confirmed executable path;
- `PalServerProcessController` — starts confirmed scripts or executables and performs guarded process operations;
- `PalServerShutdownService` — executes RCON save and normal shutdown stages;
- `PalServerMetricsCollector` and `PalServerLiveStatusCollector` — produce process and live-server snapshots;
- `ServerOperationHistoryStore` — persists completed lifecycle operations.

The confirmed launch configuration is stored separately from server credentials. A single coordinator operation lock prevents overlapping start, stop, restart, and force-stop actions.

```text
Vue operation request
    → authenticated `/api/v1/server-runtime/*` endpoint
    → role and operation-lock validation
    → `PalServerRuntimeCoordinator`
    → discovery / process locator / RCON safe-stop service
    → operation snapshot store
    → audit + `PalOpsEvent`
    → SignalR operation update
```

A process action never executes directly from a SignalR client message. SignalR distributes state; CSRF-protected HTTP endpoints perform mutations.

Normal stop and the stop phase of restart use one deterministic sequence: re-verify the locked PalServer/Shipping PID, send `Shutdown 1 Server will shut down in 1 seconds`, confirm the RCON business response, publish a live ten-second grace countdown, and return immediately if the installation has fully exited. If verified processes remain after the countdown, PalOps first terminates the originally locked PID, then cleans verified `PalServer.exe` launchers and any Shipping process created during that race, followed by a final exit check. The removed `Save`, `DoExit`, and window-close fallback stages are not part of normal stop. An unverified process is never killed. The independent Owner-only Force Stop operation remains available for cases where the RCON shutdown command could not be confirmed, with explicit confirmation and audit data.

## Metrics and health sampling

Runtime monitoring separates **collection** from **browser delivery**:

- low-frequency backend sampling continues for process identity, abnormal-exit detection, health, and alert evaluation;
- active clients default to a 10-second presentation interval and may select 5, 10, 15, 30, or 60 seconds, or manual-only mode;
- Palworld/PalDefender live REST status is cached for the configured interval (10 seconds by default), and the player list is queried only when `/metrics` does not provide the current-player count;
- one cached snapshot is reused instead of starting a collector per browser;
- manual refresh triggers one immediate snapshot;
- lifecycle and business events remain immediate even when metrics are manual-only.

A runtime snapshot may include host CPU and memory, PalServer CPU and working set, PID, thread count, uptime, save-volume free space, cached save-directory size, online/max players, and true Server FPS when a supported upstream source exposes it. Missing FPS is reported as unavailable; it is not inferred from CPU load.

Successful `HttpClientFactory` request-start/request-end records are infrastructure noise rather than operational events. `System.Net.Http.HttpClient.*` and `Microsoft.Extensions.Http` are filtered below `Warning` for console and file-backed system logs. HTTP warnings and failures remain visible, and the system-log reader also hides historical routine HTTP information records created by older builds.

## SignalR real-time delivery

`PalOpsHub` is exposed at `/hubs/palops` and requires the existing authenticated session. `RealtimeConnectionRegistry` tracks each connection's requested metric interval. `RealtimeSnapshotDispatcherService` distributes cached runtime snapshots, while player-presence and business services publish immediate events.

SignalR carries state and events such as:

- runtime snapshot changes;
- lifecycle operation stages;
- player online/offline changes;
- backup and save-index outcomes;
- notification delivery updates;
- alerts and PalDefender update availability.

The Vue client reconnects automatically and requests a fresh snapshot after reconnect. REST remains the initial-load and degraded fallback path.

## PalDefender version detection

`PalDefenderVersionService` combines:

- the current component version from the configured PalDefender endpoint;
- the latest stable, non-draft, non-prerelease GitHub release from `PalDefenderReleaseClient`;
- normalized semantic comparison through `SemanticVersionValue`;
- bounded backend caching and explicit manual refresh.

An automatic update prompt is dismissed per browser and per release tag through local storage. Manual checks always present a result. GitHub failure never prevents current-version or general overview rendering.

## PalDefender configuration management

PalDefender configuration management is implemented as an explicit local-file boundary rather than a generic file endpoint.

```text
PalDefenderPage
  -> PalDefenderConfigManager / PalDefenderConfigEditor
  -> /api/v1/paldefender/config-files/*
  -> PalDefenderConfigurationService
       -> PalDefenderConfigurationPathResolver
       -> PalDefenderConfigurationValidator
       -> backup + write-through temp + atomic replace
```

The resolver derives the same-host PalServer installation root from runtime launch configuration and allows only three core JSON files plus direct children of the three supported Pal subdirectories. The validator parses strict JSON and applies file-kind-specific types, ranges, enum values, and warnings. The service uses an expected SHA-256 token to prevent lost updates and never writes unvalidated browser content directly to the target path.

Reads are authenticated. Mutations use the `Administrator` policy, CSRF validation, and audit records. Configuration bodies are not copied into audit metadata. Activation remains an explicit operational step: the UI can invoke RCON `reloadcfg`, while settings that PalDefender cannot hot-reload require a PalServer restart.

Detailed configuration behavior is documented in [`paldefender-configuration-management.md`](paldefender-configuration-management.md). PalDefender DLL placement, first-start layout, REST tokens, firewall boundaries, upgrade, rollback, and troubleshooting are documented in [`paldefender-deployment.md`](paldefender-deployment.md).

## Notification event bus and Webhook delivery

`IPalOpsEventBus` is an in-process bounded fan-out bus built on bounded channels. Publishers emit normalized `PalOpsEvent` records after their primary transaction reaches its committed success or failure state. `WebhookDispatcherService` subscribes independently.

```text
Primary business transaction succeeds
    → bounded `IPalOpsEventBus`
    → subscription match
    → registered-variable template render
    → provider payload construction
    → SSRF-validated outbound HTTP request
    → delivery history / bounded retry
```

Webhook failure does not roll back a successful player operation, backup, save parse, or server lifecycle action.

Provider adapters cover generic JSON, WeCom, DingTalk, Feishu, Discord, Slack, and Telegram. The common pipeline validates templates against registered variables, applies provider payload rules, validates the final destination, limits timeout/redirect/body behavior, retries a bounded number of times, and stores sanitized delivery history. `NotificationAlertPolicyService` applies sustained-threshold, recovery, and quiet-period rules to avoid alert storms.

## Local persistence and atomic replacement

The release deliberately retains local file persistence. Storage is isolated behind interfaces so a later storage migration does not change HTTP contracts.

| Store | Format | Sensitive fields | Write strategy |
|---|---|---|---|
| Server settings | Versioned JSON | RCON/PalDefender secrets through existing protection | temp file + flush + atomic replace |
| Runtime configuration | Versioned JSON | confirmed paths/arguments; no plaintext passwords | temp file + flush + atomic replace |
| Runtime operations | JSONL or bounded versioned records | no command-line secrets | append/bounded rotation |
| Webhook channels | Versioned JSON | URL credentials/tokens encrypted with Data Protection | temp file + flush + atomic replace |
| Webhook history | bounded JSONL | headers/body sanitized | append/bounded rotation |
| Save index | existing local index format | derived save data | replace only after successful projection |

Configuration stores preserve a `.bak` copy where implemented. Temporary files are flushed to durable storage before replacement. Save-index snapshots retain a bounded history and keep the previous current snapshot when projection fails. Schema-version mismatches use safe defaults or fail without overwriting the original file.

## Vue workbench shell

`frontend-vue/src/layouts/WorkbenchShell.vue` is the normal desktop-oriented application shell. It provides the compact navigation rail, global server/status strip, current work area, theme control, user menu, and connection/update footer.

Normal routes are lazy-loaded and organized under:

- `src/pages` for business pages;
- `src/components/workbench` and domain component folders for reusable UI;
- `src/navigation` for the four-character Chinese menu model and role visibility;
- `src/stores` for theme, workspace, authentication, real-time, PalDefender, and map preferences;
- `src/api` for authenticated HTTP and CSRF behavior;
- `src/styles` for shared design tokens and compact desktop rules.

The shell targets 1366×768 through 1920×1080 desktop operation. Tables and work regions own their scroll boundaries instead of expanding the document indefinitely.

## MapLibre map workspace

`frontend-vue/src/features/map/pages/WorldMapPage.vue` is a normal `WorkbenchShell` child for `/map`. It composes a persistent left layer tree, a MapLibre canvas, a wide-screen overview/search/details panel, and a drawer below 1440px.

`MapCanvas.vue` owns one MapLibre instance and its resize/removal lifecycle. The authenticated shell preloads the map route while idle and keeps `WorldMapPage` alive after its first visit. The initial data/render path uses parallel acquisition and an explicit style-scaffold barrier:

```text
Workbench idle preload
    → route setup primes a guild-base coordinate when present
    → guild-base/custom-marker, player, and locale POI requests start in parallel
    → MapLibre empty style reaches `load`
    → raster, runtime, selection, and requested static layer scaffolds are installed
    → any state received before that point is flushed from the pending-state latch
    → runtime and fixed GeoJSON sources render independently of raster-tile completion
    → `runtime-ready` and `static-ready` are emitted only after actual render frames
```

Adding the raster source after MapLibre's initial `load` can temporarily make `map.isStyleLoaded()` false. This state describes outstanding raster/style work, not whether existing GeoJSON sources are writable. Map update functions therefore gate on the application's `styleScaffoldReady` flag and retain early updates in `pendingMapStateFlush`; they never discard runtime entities or default POIs during tile loading. `styledata` performs a bounded retry only when pending state exists.

The 1,242 fixed POIs are stored as three locale-aligned frontend JSON files and rendered through one local GeoJSON source. Runtime and static acquisition run in parallel. Only currently visible fixed-category layers are created during first entry, while hidden categories remain lazy. Only players, guild bases, and custom markers are loaded from authenticated runtime APIs into separate GeoJSON sources. `WorldMapPage` displays localized engine/runtime/static milestones until both the server markers and the default-selected fixed markers have produced a render frame.

Palpagos and World Tree keep independent viewports and visible-layer preferences. Simplified Chinese, English, and Japanese switch local names and search aliases without rebuilding the whole page. Restore Defaults enables all server runtime layers plus Fast Travel and Towers; all other fixed categories remain available but disabled to control density. Map-data attribution and license information is displayed under About System.

The old backend map package, health-check, fixed-POI, projection, import, and server-side progress services are removed. Production map startup uses no active-package, PBF, manifest, source, search, coordinate, tile-health, or progress endpoint. Exploration progress is browser-local.

## Failure isolation

The system treats each background capability as independently degradable:

- a failed save parse leaves the previous index active;
- unavailable official REST or PalDefender fields remain visibly unavailable while local data continues to render;
- a SignalR disconnect falls back to REST and reconnects without enabling mutations over the hub;
- a Webhook failure is queued/history-recorded but cannot change the primary action result;
- a normal stop may clean up only freshly re-located, identity-verified residual processes after the one-second RCON shutdown command is confirmed and the visible ten-second grace countdown expires; a failed or unverified RCON shutdown leaves the process untouched and reports an actionable error;
- damaged history lines are skipped without making current state unreadable;
- GitHub release lookup failure does not block the overview.

Operational errors use stable codes and trace identifiers so the UI, audit log, system log, and GitHub issue reports can refer to the same failure.

## Security boundaries

- PalOps Web is intended for a trusted private network or authenticated reverse proxy, not direct public exposure.
- Backend authorization and CSRF validation protect every mutation; frontend visibility does not grant permission.
- Process control is restricted to the confirmed local `PalServer.exe` identity and Windows-local paths.
- Runtime configuration cannot submit arbitrary one-off shell commands from the browser.
- RCON, PalDefender, Webhook tokens, and URL credentials are protected or redacted at persistence and response boundaries.
- Webhook destinations pass SSRF validation; local/private destinations require an explicit allowed configuration and are still revalidated after provider payload construction.
- Logs, audit records, operation history, and delivery history exclude passwords, tokens, authorization headers, and command-line secrets.
- Save parsing operates on private snapshots with bounded inputs and never writes to the live world directory.
- Offline map assets are local files validated by tooling; runtime map code does not fetch remote tiles.
