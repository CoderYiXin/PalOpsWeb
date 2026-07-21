# Third-Party Notices

PalOps Web is an independent community implementation. References below do not imply endorsement, affiliation, or ownership transfer. Exact shipped package versions are recorded in the project and lock files named below.

## Vue.js

- Package: `vue`
- Shipped version: recorded in `frontend-vue/package-lock.json`
- License: MIT
- Use: reactive UI runtime and component framework.

## Vue Router

- Package: `vue-router`
- Shipped version: recorded in `frontend-vue/package-lock.json`
- License: MIT
- Use: authenticated workbench routes and the independent immersive-map route.

## Vue I18n

- Package: `vue-i18n`
- Shipped version: recorded in `frontend-vue/package-lock.json`
- License: MIT
- Use: Simplified Chinese, English, and Japanese user-interface messages.

## Element Plus

- Package: `element-plus`
- Shipped version: recorded in `frontend-vue/package-lock.json`
- License: MIT
- Use: accessible form, table, dialog, navigation, and feedback primitives.

## Element Plus icons for Vue

- Package: `@element-plus/icons-vue`
- Shipped version: recorded in `frontend-vue/package-lock.json`
- License: MIT
- Use: application navigation and operation icons.

## MapLibre GL JS

- Package: `maplibre-gl`
- Shipped version: `5.24.0`
- License: BSD-3-Clause
- Use: local raster basemaps, PBF vector sources, GeoJSON runtime entities, layers, clustering, symbols, and viewport interaction.

The corresponding BSD-3-Clause text is retained in `licenses/MapLibre-GL-JS-BSD-3-Clause.txt`.

## Microsoft ASP.NET Core SignalR JavaScript client

PalOps Web uses `@microsoft/signalr` in the Vue frontend for authenticated runtime and event delivery. The package is distributed under the MIT License. The exact shipped version is recorded in `frontend-vue/package-lock.json`.

## Vite and Vue plugin

- Packages: `vite`, `@vitejs/plugin-vue`
- Shipped versions: recorded in `frontend-vue/package-lock.json`
- License: MIT
- Use: frontend development and production asset compilation.

These are build-time dependencies and are not a separately hosted service at runtime.

## TypeScript and Vue TypeScript compiler

- Packages: `typescript`, `vue-tsc`
- Shipped versions: recorded in `frontend-vue/package-lock.json`
- TypeScript license: Apache-2.0
- Vue TypeScript compiler license: MIT
- Use: static type checking and Vue production-build validation.

## SnosMe/ooz-wasm and ooz

- Project: https://github.com/SnosMe/ooz-wasm
- Integrated package: `ooz-wasm` 2.0.0
- License: GNU GPL-3.0-or-later
- Use in PalOps Web: the embedded `ooz.wasm` module provides in-process Kraken decompression for Palworld 1.0 `PlM1` saves.
- Corresponding source: `third_party/ooz/source/`
- Original npm artifact: `third_party/ooz/ooz-wasm-2.0.0.tgz`
- Source archive and build instructions: `third_party/ooz/ooz-wasm-2.0.0-source.tar.gz`, `third_party/ooz/README.upstream.md`
- Integrity inventory: `third_party/ooz/SHA256SUMS`

The integrated distribution is therefore released under GPL-3.0-or-later. The decoder is hosted in the ASP.NET Core process and does not require a proprietary Oodle DLL.

## Bytecode Alliance Wasmtime for .NET

- Project: https://github.com/bytecodealliance/wasmtime-dotnet
- NuGet package: `Wasmtime` 44.0.0
- License: Apache-2.0 WITH LLVM-exception
- Local license copy: `licenses/Wasmtime-Apache-2.0-WITH-LLVM-exception.txt`
- Use in PalOps Web: loads and executes the embedded `ooz.wasm` module and supplies its Emscripten memory imports.

The normal NuGet publish process supplies platform-specific Wasmtime native runtime files. Operators must deploy the complete publish directory.

## deafdudecomputers/PalworldSaveTools / palsav-flex

- Project: https://github.com/deafdudecomputers/PalworldSaveTools
- Relevant component: `src/palsav` (`palsav-flex`)
- Component license: GNU GPL-3.0-or-later
- Use in PalOps Web: reference and adaptation for the current Palworld SAV envelope boundary, logical-property-path propagation, map/set `StructProperty` type inference, and the current Palworld path type-hint table.
- Modification statement: PalOps Web does not bundle or launch the PalworldSaveTools Python/PySide application. The relevant read-only parser behavior, type hints, and selected coordinate records are implemented or stored in C#, while `PlM1` decompression is provided by the separately attributed GPL `ooz-wasm` module.

## zaigie/palworld-server-tool

- Project: https://github.com/zaigie/palworld-server-tool
- License: Apache License 2.0
- Local license copy: `licenses/palworld-server-tool-APACHE-2.0.txt`
- Use in PalOps Web: feature-boundary research, server-management workflow reference, catalog/source comparison, and attribution.
- Modification statement: PalOps Web uses a separate ASP.NET Core/Vue implementation and does not call PST APIs at runtime. Any catalog data or locally served assets derived from earlier project material remain attributed to their source and rights holders.

## Ultimeit/PalDefender

- Project: https://github.com/Ultimeit/PalDefender
- License of the public repository material: MIT
- Local license copy: `licenses/PalDefender-MIT.txt`
- Use in PalOps Web: interoperability with the documented REST API, RCON commands, configuration file types/schemas, and current/latest version comparison against public release metadata.
- PalDefender itself is a separately installed server component and is not bundled in this source package.

## cheahjs/palworld-save-tools

- Project: https://github.com/cheahjs/palworld-save-tools
- License: MIT
- Local license copy: `licenses/palworld-save-tools-MIT.txt`
- Use in PalOps Web: public SAV/GVAS format interoperability research and supported Palworld structure comparison.
- Modification statement: PalOps Web does not bundle or execute the Python project. The save reader in this repository is an independent C# implementation and does not write player, guild, inventory, or Pal SAV data.

## Paldeck community database

- Website: https://paldeck.cc/
- Use: optional catalog research and ID/name comparison.
- Paldeck is a community project and is not affiliated with Pocketpair.
- PalOps Web does not contact Paldeck at runtime.

## PalOps built-in core POI snapshot

- Package: `palops-default-core`
- Version: `2026.07.4`
- Compilation license: CC BY-SA 4.0
- Included output: normalized per-locale POI JSON, localized labels, search aliases, and category metadata served as frontend-static assets.
- Public factual-coordinate references: PinDrop Palworld listings / paldb-derived marker listings.
- Attribution reference: Palworld Wiki map material under CC BY-SA 4.0.
- Implementation and coordinate-method reference: ARXII-13/Palworld-Interactive-Map under Apache-2.0.
- Current fast-travel coordinate supplement: `deafdudecomputers/PalworldSaveTools` revision `e4e1439b274c1140eed5690051ce59ab14b68027`, `resources/game_data/fast_travel_points.json`, under MIT; local license copy: `licenses/palworld-save-tools-MIT.txt`.

The bundled snapshot contains no remote scripts or copied third-party marker icons. PalOps assigns its own stable IDs, uses its own generated marker symbols, and serves the resulting data locally. Palworld names and underlying game-derived facts remain subject to Pocketpair's rights and applicable law.

## Offline map source

The checked-in map dataset metadata identifies **The Hidden Gaming Lair** as the source page for the local Palpagos and World Tree imagery. The metadata explicitly states that PalOps Web does not grant redistribution rights and that authorization must be verified before publishing downloaded tiles.

The source-code license does not grant rights to redistribute game-derived map imagery. Release maintainers must remove the tiles or document separate authorization when redistribution is not permitted.

## Palworld names, marks, and game assets

Palworld and associated names, logos, characters, screenshots, icons, map imagery, and other game assets belong to Pocketpair or their respective rights holders. This independent community tool is not endorsed by Pocketpair.

Before redistributing catalog images, map images, or other game-derived assets, repository maintainers must verify that redistribution is permitted in their jurisdiction and deployment context. Unverified assets should be removed or replaced by user-supplied local files before public release.

## Dependency inventories

Direct and build dependencies and their exact versions are declared in:

- `src/PalOps.Web/PalOps.Web.csproj`
- `tools/PalOps.Tooling/PalOps.Tooling.csproj`
- `frontend-vue/package.json`
- `frontend-vue/package-lock.json`

Their licenses remain governed by the corresponding upstream packages. Release maintainers must run the repository release verifier and update this notice when direct dependencies change.
