> Language: [简体中文](release-checklist.md) | **English**

# Release Checklist

This checklist applies to the supported Windows `win-x64` PalOps Web release.

## 1. Clean source tree

- No files exist under `src/PalOps.Web/data/` except `.gitkeep`.
- No `.sav`, `.db`, `.sqlite`, `.pdb`, user settings, passwords, tokens, cookies, or signed webhook URLs are present.
- `frontend-vue/package-lock.json` matches `frontend-vue/package.json`.
- Version metadata in `frontend-vue/package.json`, `PalOps.Web.csproj`, About System, release assets, and documentation is `1.1.0`.
- All screenshots use fabricated server, account, player, guild, and webhook data.

## 2. Required toolchain

- .NET 10 SDK
- Node.js 22 LTS or newer
- npm supplied with Node.js
- PowerShell 7 recommended; Windows PowerShell 5.1 remains acceptable when the scripts pass

## 3. Source verification

```powershell
.\scripts\build.ps1
```

The command must complete all of the following:

1. `npm ci`
2. Vue TypeScript checking
3. Vue production build into `src/PalOps.Web/wwwroot`
4. `npm audit --audit-level=high`
5. `.NET restore` and Release build
6. catalog verification
7. map-definition verification
8. documentation verification
9. release-tree secret and file-type verification

## 4. Runtime smoke matrix

| Area | Required result |
|---|---|
| Startup | Console shows version, environment, data directory, save directory, listening URLs, and startup completion without secrets. |
| Authentication | Owner, Administrator, Operator, Auditor, and Viewer retain their intended permissions. |
| Runtime discovery | A local save path discovers the expected PalServer root and presents script/EXE candidates for confirmation. |
| Start safety | Repeated Start requests do not create a second managed PalServer instance. |
| Normal stop | The current PID identity is verified; `Shutdown 1 Server will shut down in 1 seconds` is accepted; the operation dialog displays a live 10-to-1 second countdown; remaining verified engine PID, launcher, and race-created Shipping residue are removed; final state is Stopped. |
| Force stop | Only Owner can invoke it after typed confirmation; the action is audited. |
| Metrics and logs | System and PalServer metrics render; the default presentation/live-status interval is 10 seconds; `manual` produces no periodic metric snapshots; successful typed-HttpClient lifecycle records do not flood console or System Logs, while warnings/errors remain visible. |
| Real-time events | Player presence, operations, backups, and alerts continue immediately in every refresh mode. |
| PalDefender | Manual version check works; automatic prompt appears once per browser per release tag; installation, REST token, firewall and rollback checks follow [`paldefender-deployment.md`](paldefender-deployment.md). |
| Webhooks | Test delivery, encrypted secrets, retry history, and SSRF restrictions work. |
| Save projection | Teammate bases appear or are retained as unresolved instead of being discarded. |
| Coordinates | The frontend transform resolves `158474.03, -60787.92, -832.60` to Palpagos near `-476.66, 615.17`; invalid coordinates are not rendered outside the map, and no backend projection/health endpoint is called. |
| Map | `/map` stays inside `WorkbenchShell`; server entities render in the first runtime frame before fixed-POI fetch/parse/layer creation; 1,242 frontend-local POIs remain available across 30 nonempty categories; `zh-CN` uses Chinese labels; Restore Defaults selects only server data, Fast Travel, and Towers. |
| Themes | Light is default; dark preference survives reload. |
| Viewports | Normal pages are usable at 1366×768 and 1920×1080; map remains full-screen. |

### PalDefender configuration manager

- [ ] List/read succeeds for the allowlisted PalDefender files on a non-production server.
- [ ] Viewer/Auditor cannot mutate configuration; Owner/Administrator can validate and save.
- [ ] Invalid JSON, wrong field types, path traversal, unknown directories, reparse points, and files over 2 MiB are rejected.
- [ ] A stale SHA-256 value prevents overwriting an externally modified file.
- [ ] Successful save creates a timestamped backup and the saved file can be parsed again.
- [ ] Audit records contain metadata but no configuration body or secret values.
- [ ] `reloadcfg` or restart applies the test change, then the original configuration is restored.

### World-map runtime fast path

- [ ] After login, the workbench preloads the map route during an idle period without navigating.
- [ ] Entering `/map` starts guild-base/custom-marker and player requests during setup, before MapLibre load completion.
- [ ] Throttle/offline-test raster tiles and confirm runtime entities plus default-selected fixed markers still appear without refreshing or toggling a checkbox.
- [ ] Confirm the first-entry loading overlay advances through engine, server-data, server-marker, static-data, and static-marker stages, then closes only after render acknowledgements.
- [ ] Guild bases and custom markers appear even when the player/PalDefender endpoint is delayed.
- [ ] Navigating from a guild base places the known coordinate immediately, focuses it after activation, and reuses prefetched/in-flight requests.
- [ ] Leave `/map`, return through the sidebar, and confirm the retained MapLibre instance resizes and displays cached/runtime data without rebuilding the complete workspace.
- [ ] `/api/v1/map/entities` runtime requests do not enumerate or hash raster tiles.
- [ ] The map canvas no longer renders the removed English game-imagery warning.

### HTTP logging and polling

- [ ] With all upstream REST endpoints healthy, the console and System Logs do not continuously record `System.Net.Http.HttpClient.*` request-start/request-end information lines.
- [ ] A simulated HTTP timeout or 500 response remains visible as Warning/Error with useful endpoint and trace context.
- [ ] Default browser/runtime status delivery uses 10 seconds and does not query the player list when `/metrics` already provides `currentplayernum`.

## 5. Strict publish

Both offline map layers must be present before packaging:

```powershell
.\scripts\publish-win-x64.ps1 -Version 1.1.0
```

Expected outputs:

- `artifacts/palops-web-1.1.0-win-x64/`
- `artifacts/palops-web-1.1.0-win-x64.zip`
- `artifacts/palops-web-1.1.0-win-x64.sha256`

## 6. Archive inspection

The extracted archive must start with `start.cmd`, preserve all required notices, contain current `wwwroot`, and contain no runtime `data` files. Verify the ZIP hash against the adjacent `.sha256` file before upload.

## 7. GitHub release

- Release tag for this plan: `v1.1.0`
- Attach the ZIP and its `.sha256` file.
- Paste the matching `CHANGELOG.md` section into the release notes.
- State that lifecycle control is Windows-local-only.
- State that operators should back up the save and data directories before upgrading.
