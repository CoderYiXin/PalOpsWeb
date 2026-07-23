> Language: [简体中文](deployment.md) | **English**

# Deployment Guide

## Supported topology

PalOps Web, PalServer, the configured save directory, and the managed `PalServer.exe` must be on the same Windows host. Network shares and remote process control are not supported by the runtime-management feature.

The web UI may be accessed from another machine through a trusted LAN or a correctly configured HTTPS reverse proxy, but process discovery and lifecycle operations always execute on the PalServer host.

```text
Administrator browser
  → trusted LAN, VPN, or HTTPS reverse proxy
  → PalOps Web on the PalServer Windows host
      → local PalServer process
      → local world save directory
      → loopback/private Palworld REST, PalDefender REST, and RCON
```

Do not expose PalOps management, Palworld REST, PalDefender, or RCON ports directly to the public Internet.

## Obtain a release

Use the official `win-x64` ZIP and verify its adjacent SHA-256 value before extraction. For a source build, use the canonical commands in [`build.md`](build.md) and publish only through:

```powershell
.\scripts\publish-win-x64.ps1 -Version 1.3.1
```

## First deployment

1. Extract the complete release into a dedicated directory on the PalServer host.
2. Keep `start.cmd`, `wwwroot`, documentation, notices, and all published runtime files together.
3. Give the PalOps service identity read access to the selected world directory.
4. Give it write access to the PalOps `data` directory, Data Protection key directory, and configured backup directory.
5. Set `PALOPS_ADMIN_PASSWORD` only for first initialization, start PalOps once, then remove the environment variable.
6. Confirm the save directory, Palworld REST, PalDefender, and RCON settings in the UI.
7. Review automatically discovered scripts and `PalServer.exe`; an Owner must confirm the lifecycle configuration before mutations are enabled.
8. Restrict the Windows firewall rule to the management subnet.

## Windows service hosting

PalOps Web can be run under Windows Service Manager, Task Scheduler, NSSM, or another supervised Windows process manager. The host identity must be able to inspect and control the configured PalServer process. Avoid an interactive user profile dependency in paths or credentials.

The application restores console output when launched interactively. Service hosts should retain standard output or the PalOps file logs for diagnosis.

## Save-directory permissions

Indexing requires read access to:

```text
Pal\Saved\SaveGames\0\<WorldId>\Level.sav
Pal\Saved\SaveGames\0\<WorldId>\Players\
```

Backup creation requires read access to the world directory and write access to the backup directory. Restore requires temporary write access to the world directory parent. PalServer lifecycle control also requires local process access and a working RCON configuration for safe stop.

## Guarded restore procedure

1. Stop the Palworld dedicated server safely.
2. Confirm that no server process holds the world directory.
3. Enable restore in PalOps settings.
4. Create `restore-server-stopped.flag` in the PalOps data directory when required by the current build.
5. Verify the selected backup.
6. Run restore preflight.
7. Enter `CONFIRM` and the exact backup filename.
8. Allow PalOps to create a pre-restore backup and atomically replace the world directory.
9. Review audit and system logs before restarting PalServer.

Validate restore on a non-production server before relying on it operationally.

## PlM1 save decoding

Palworld saves stored as `PlM1` are decoded inside the ASP.NET Core process with the bundled GPL `ooz.wasm` module hosted by Wasmtime. Use the complete publish output; copying only `PalOps.Web.dll` and `wwwroot` is insufficient. Retain `LICENSE`, `NOTICE`, `THIRD-PARTY-NOTICES.md`, and `third_party/ooz/` when redistributing source or binaries.

## PalDefender configuration permissions

Install PalDefender and validate its REST/token/firewall setup before enabling configuration writes. See [`paldefender-deployment.md`](paldefender-deployment.md) for the complete Windows installation, upgrade, rollback, and troubleshooting procedure.

The PalOps Windows identity must be able to read `Pal/Binaries/Win64/PalDefender`. To enable online create/update/delete operations, grant only the file-create, write, rename, and delete permissions required for the allowlisted JSON files and `.palops-backups`; do not grant unnecessary write access to the entire PalServer installation.

Before an upgrade or service-account change, back up:

```text
Pal/Binaries/Win64/PalDefender
Pal/Binaries/Win64/PalDefender/.palops-backups
```

After deployment, verify that a read-only user can view configuration, an Administrator can validate and save a non-production change, an invalid JSON document is rejected, and `reloadcfg` or restart applies the saved configuration. See [`paldefender-configuration-management.md`](paldefender-configuration-management.md).

## Runtime polling and HTTP logs

The default live-status and browser presentation cadence is 10 seconds. Palworld `/info` and `/metrics` results are cached for that interval, and the player-list aggregation path is used only when `/metrics` does not provide the current-player count. This avoids the former two-second request burst.

Successful `System.Net.Http.HttpClient.*` and `Microsoft.Extensions.Http` request lifecycle records are filtered below `Warning`, so healthy polling does not flood the interactive console or System Logs. HTTP warnings, timeouts, non-success responses, and exceptions remain visible. For a temporary transport-level diagnosis, raise those two categories to `Information` in `appsettings.json`, reproduce once, then restore `Warning`; do not leave verbose HTTP logging enabled in normal operation.

## Reverse proxy

Terminate TLS at a trusted reverse proxy and preserve WebSocket upgrades for SignalR. Configure forwarded headers only for explicit proxy addresses. Do not trust arbitrary public `X-Forwarded-*` headers.

Cookies, CSRF protection, and SignalR authentication assume one trusted origin. Review proxy path rewriting and secure-cookie settings before exposing the UI outside the local machine.

## Upgrade procedure

1. Use PalOps to create and verify a save backup.
2. Back up the PalOps `data` directory and Data Protection key directory.
3. Stop PalOps Web. Do not delete the existing `data` directory.
4. Extract the new release into a new directory.
5. Copy the previous `data` directory and Data Protection keys into the corresponding configured locations.
6. Start the new release and inspect console startup diagnostics.
7. Confirm login, save indexing, runtime identity, Webhook channel state, both raster maps, the 1,251 frontend-local fixed POIs, Chinese labels, default layer selection, and runtime player/base markers before removing the old release directory.
8. Roll back by stopping the new release and starting the old directory with the untouched backups.

Do not overwrite the only known-good installation in place. Keep the previous release directory until the smoke matrix is complete.

## Release verification

Run the full checklist in [`release-checklist.md`](release-checklist.md). The strict publication gate requires both Palpagos and World Tree local raster layers. Fixed POIs, coordinate transforms, visibility state, and exploration progress are frontend-local in version 1.3.1; preserving the browser profile retains local exploration state. There is no backend map package, activation record, tile health scan, or map import directory to migrate. Server FPS must display `暂不支持` when no configured authoritative endpoint exposes an FPS field.
