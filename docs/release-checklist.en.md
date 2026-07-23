> Language: [简体中文](release-checklist.md) | **English**

# Release Checklist

This checklist applies to the PalOps Web **1.3.1** GitHub source publication and Windows `win-x64` release.

## 1. Clean source tree

- [ ] `README.md` is the Chinese project homepage and `README.en.md` is the matching English homepage.
- [ ] The Chinese README references only `docs/images/zh-CN/`; the English README references only `docs/images/en-US/`.
- [ ] Each locale contains 37 product screenshots (74 total), all **1920×1080 WebP**, rendered from the current Vue UI with fabricated data.
- [ ] Public `docs/*.md` files have matching `.en.md` counterparts.
- [ ] Historical design drafts, internal implementation plans, one-off defect reports, packaging audits, and temporary capture scripts are absent.
- [ ] `node_modules`, `bin`, `obj`, `dist`, `.git`, caches, runtime data, logs, and secrets are absent.
- [ ] Version metadata, documentation, publish commands, and release tag are `1.3.1`.
- [ ] `.github/workflows/build.yml` passes on GitHub Actions.

## 2. Canonical build

```powershell
.\scripts\build.ps1
```

The command must complete:

1. lockfile-based dependency installation;
2. frontend feature and publication contracts;
3. Vue TypeScript checking;
4. Vite production compilation;
5. npm high-severity auditing;
6. .NET 10 restore/build;
7. catalog, map, documentation, and release-tree verification.

## 3. Runtime smoke matrix

| Area | Required result |
|---|---|
| Startup | Console reports version, environment, data/save directories, and listening URLs without secrets. |
| Authorization | Owner, Administrator, Operator, Auditor, and Viewer permissions match navigation; backend policy cannot be bypassed. |
| Overview/statistics | Process, host, online-player, FPS, version, save, and historical trend data render correctly. |
| Players/guilds | Live/indexed players, guild membership, base ownership evidence, and map navigation work. |
| World map | Palpagos / World Tree offline maps, 94 Fast Travel markers, live players, and bases render correctly. |
| Map refresh | Player refresh supports 1/2/3/5/10/15/30 seconds with a 3-second default; bases/custom markers remain on an independent 30-second schedule. |
| Configuration | Structured/raw editing, validation, diff, backup, atomic persistence, and safe restart work. |
| Maintenance/Crash Guard | Maintenance orchestration, recovery, health verification, and circuit status are correct. |
| Normal stop | `Shutdown 1` succeeds, a 10-to-1 countdown is shown, verified residual PIDs are cleaned up, and final state is Stopped. |
| Force stop | Owner-only, audited, with process identity revalidation. |
| RCON | High-risk commands require acknowledgement; formatting and response history are correct. |
| Saves | Indexing, backup, restore preflight, and differences remain protected; failures retain the last good index. |
| Player discipline | Whitelist, bans, identities, violations, and kick history are complete; UserId validation blocks incorrect writes. |
| Plugins/mods | Inventory, versions, dependencies, compatibility, backups, and rollback information are available. |
| Notifications | Channel tests, templates, retry, and delivery history work with secrets redacted. |
| Themes/viewports | Light is default, dark persists, and 1366×768 plus 1920×1080 are usable. |

## 4. World-map checks

- [ ] Palpagos and World Tree configuration and raster tiles are present.
- [ ] All three locale indexes contain 1,251 POIs and 30 nonempty static categories.
- [ ] The local Fast Travel layer contains 94 records and explicitly shows the gap to 149 known upstream records.
- [ ] Restore Defaults enables only server data, Fast Travel, and Towers.
- [ ] Player markers render above fixed POIs while a co-located Fast Travel marker remains discoverable.
- [ ] Runtime entities and default POIs render after the layer scaffold is ready even when raster tiles are throttled.
- [ ] The backend does not enumerate, hash, or health-check raster tiles.

## 5. PalDefender checks

- [ ] Installation directory and DLL placement are correct.
- [ ] REST uses a real token rather than `TokenExample.json`.
- [ ] Whitelist operations prefer `whitelist_add` / `whitelist_remove` and verify `WhiteList.json`.
- [ ] Strict UserId validation prevents player names and save PlayerUID values from being written by mistake.
- [ ] Path allowlists, JSON types, file size, traversal, and reparse-point checks are effective.
- [ ] A stale SHA-256 blocks overwrite of external changes.
- [ ] Successful persistence creates a parseable backup.
- [ ] Audit records contain no configuration bodies or secrets.

## 6. Strict publish

```powershell
.\scripts\fetch-map-tiles.ps1 -Layer all
.\scripts\publish-win-x64.ps1 -Version 1.3.1
```

Expected outputs:

- `artifacts/palops-web-1.3.1-win-x64/`
- `artifacts/palops-web-1.3.1-win-x64.zip`
- `artifacts/palops-web-1.3.1-win-x64.sha256`

The extracted archive must start with `start.cmd`, contain the current `wwwroot`, preserve required notices, and contain no runtime data or secrets.

## 7. GitHub release

- [ ] Create tag `v1.3.1`.
- [ ] Attach the ZIP and adjacent SHA-256 file.
- [ ] Use the 1.3.1 section of `CHANGELOG.md` as the release-note basis.
- [ ] State that lifecycle control is Windows-local-only.
- [ ] Require a backup of saves, PalOps data, and PalDefender configuration before upgrade.

See [PalDefender deployment](paldefender-deployment.en.md) for the detailed integration checklist.
