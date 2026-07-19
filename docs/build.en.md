> Language: [简体中文](build.md) | **English**

# Build Guide

## Requirements

- .NET 10 SDK
- Node.js 22 LTS or newer
- npm supplied with Node.js
- PowerShell 7 recommended

## Source layout

- `frontend-vue/`: editable Vue 3 + TypeScript source.
- `src/PalOps.Web/`: ASP.NET Core host and backend services.
- `src/PalOps.Web/wwwroot/`: compiled frontend files served by ASP.NET Core.
- `tools/PalOps.Tooling/`: catalog, map, documentation, manifest, and release verification commands.
- `scripts/`: canonical build, map-fetch, and Windows publication entry points.

The compiled `wwwroot` files are retained in source archives. Rebuilding the frontend replaces their hashed assets.

## Canonical verification

```powershell
.\scripts\build.ps1
```

Do not use `dotnet build` alone as release evidence because it does not rebuild the Vue application, verify documentation, audit npm dependencies, or run repository tooling.

The script performs frontend dependency installation, TypeScript checking, production compilation, npm auditing, .NET restore/build, catalog verification, map verification, documentation verification, and source-tree release verification. The frontend build also enforces the 1.1.0 map contract: 1,242 aligned locale records, at least 30 nonempty fixed categories, Chinese `zh-CN` display names, Restore Defaults limited to runtime data plus Fast Travel and Towers, source/license attribution on About System, idle route preloading, parallel runtime/static acquisition, pending-state recovery while raster tiles load, render-frame readiness milestones, a localized first-entry loading overlay, and lazy hidden-category construction. It also verifies the 10-second live-status default and routine typed-HttpClient log suppression.

## Manual diagnostic sequence

Use individual commands only to diagnose a failed stage:

```powershell
cd frontend-vue
npm ci
npm run typecheck
npm run build
npm audit --audit-level=high
cd ..

dotnet restore .\PalOpsWeb.slnx
dotnet build .\PalOpsWeb.slnx -c Release --no-restore
dotnet run --project .\tools\PalOps.Tooling -c Release --no-build -- catalog verify --root .
dotnet run --project .\tools\PalOps.Tooling -c Release --no-build -- map verify --root . --allow-missing
dotnet run --project .\tools\PalOps.Tooling -c Release --no-build -- docs verify --root .
dotnet run --project .\tools\PalOps.Tooling -c Release --no-build -- release verify --root .
```

This repository intentionally contains no unit-test project and no JavaScript unit-test runner. Release evidence consists of strict TypeScript checking, a production frontend build, backend compilation, dependency audit, repository validators, and the operator smoke matrix in [`release-checklist.md`](release-checklist.md).

### Fixed POI generation

The fixed-POI baseline is committed as reviewed JSON under `frontend-vue/public/map/data`. The repository no longer contains a Python generator. After editing POI data, run `npm run verify:map-complete-local` and `npm run build`. The cacheable assets remain outside `src/features/map`, contain 1,242 records per runtime locale, and keep all 30 categories nonempty.

## Offline map resources

The source tree contains map definitions, calibration samples, and fetch/verification tooling. Downloaded game-derived WebP tiles remain local and are ignored by Git:

```powershell
.\scripts\fetch-map-tiles.ps1 -Layer all
dotnet run --project .\tools\PalOps.Tooling -c Release --no-build -- map verify --root . --require-layers palpagos,world-tree
```

The fetcher writes Palpagos and World Tree tiles beneath `frontend-vue/public/map/tiles/{layer}/{z}/{x}/{y}.webp`, validates WebP content, and writes a SHA-256 `dataset.json`. Source and CI verification may use `--allow-missing`; strict publication requires both complete layers.

PalOps Web does not grant redistribution rights for downloaded imagery. Confirm that you are authorized to redistribute the selected map dataset before attaching it to a public release.

## Windows publication

The supported lifecycle-control release target is Windows `win-x64`:

```powershell
.\scripts\fetch-map-tiles.ps1 -Layer all
.\scripts\publish-win-x64.ps1 -Version 1.1.0
```

The publication script rebuilds and verifies the project, publishes the ASP.NET Core application, stages required documentation and notices, writes an internal SHA-256 manifest, creates the release ZIP, and writes the adjacent ZIP checksum.

Linux may be used as a source-build environment when the .NET and Node toolchains are available, but PalServer discovery and lifecycle control are supported only when PalOps Web runs on the same Windows host as PalServer.
