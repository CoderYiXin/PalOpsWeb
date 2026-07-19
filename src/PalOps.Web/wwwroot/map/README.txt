PalOps offline world-map tiles
==============================

Do not place a single world-map image here. PalOps uses two local WebP tile pyramids:

  tiles/palpagos/{z}/{x}/{y}.webp
  tiles/world-tree/{z}/{x}/{y}.webp

Acquire and validate the current Palworld 1.0 resources from the repository root:

  .\scripts\fetch-map-tiles.ps1 -Layer all
  dotnet run --project .\tools\PalOps.Tooling -- map verify --root . --require-layers palpagos,world-tree

Vite copies this directory to src/PalOps.Web/wwwroot/map. PalOps never loads a remote
tile at runtime. If dataset.json or tiles are missing, the map page reports the exact
state and keeps the coordinate/marker workspace available.

The downloaded map imagery is game-derived third-party material. It is intentionally
ignored by Git. PalOps does not grant redistribution rights; verify authorization
before publishing or redistributing the downloaded tiles.
