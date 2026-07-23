> Language: [简体中文](map-data-package.md) | **English**

# Frontend Static Map Bundle

## Runtime boundary

PalOps Web 1.3.0 does **not** support server-side map package upload, import, signature validation, activation, rollback, health checks, or fixed-POI APIs. Those legacy backend paths were removed.

The production map is shipped as part of the compiled Vue application:

```text
frontend-vue/src/features/map/data/
├─ map-bootstrap.ts
└─ map-icon-catalog.ts

frontend-vue/public/map/
├─ data/
│  ├─ default-pois.zh-CN.json
│  ├─ default-pois.en-US.json
│  └─ default-pois.ja-JP.json
└─ tiles/
   └─ raster layers and local tile assets
```

The frontend owns:

- Palpagos and World Tree layer descriptors;
- parallel runtime/static startup with a style-scaffold pending-state flush, so data arriving while raster tiles load is rendered without waiting for a later refresh or checkbox toggle;
- 682 local raster tiles;
- 1,251 fixed POIs in each supported locale;
- categories, icons, aliases, search, visibility state, and layer bounds;
- world-to-map coordinate transforms and runtime bounds checks;
- browser-local exploration progress.

The backend owns only dynamic server information: players, guild bases, and custom markers. `/api/v1/map/entities` returns raw world coordinates or direct map coordinates; the browser projects them with the bundled layer configuration.

## Updating map content

A map-content update is a frontend source update:

1. update the local tiles, layer metadata, icons, or committed POI JSON under `frontend-vue`;
2. run `npm run verify:map-complete-local`;
3. run `npm run build`;
4. deploy the newly compiled `src/PalOps.Web/wwwroot` together with the release.

No administrator upload screen, backend map-data directory, package manifest, signature file, version store, or activation endpoint is involved.

## Verification

```powershell
cd frontend-vue
npm run verify:map-frontend-local
npm run verify:map-complete-local
npm run verify:map-runtime-fast-path
npm run build
```

The release gate verifies that fixed map rendering remains usable when runtime APIs are unavailable and that no `/api/v1/map-data/*`, backend map projection, basemap health scan, or map import implementation is reintroduced.
