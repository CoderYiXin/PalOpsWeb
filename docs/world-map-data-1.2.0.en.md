> Language: [简体中文](world-map-data-1.2.0.md) | **English**

# World Map Data Baseline — PalOps Web 1.2.0

## Runtime architecture

Version 1.2.0 treats fixed map data as frontend-local application data. The browser loads map configuration, the category tree, icons, search aliases, localized marker names, exploration state, and all fixed POIs from the compiled Vue bundle. Only players, guild bases, and custom markers are loaded from PalOps server APIs.

The fixed dataset uses one MapLibre GeoJSON source. There is no production PBF/fallback switch and no fixed-map package request in the first-render path.

## 1.2.0 runtime layer and refresh policy

- Player markers use a dedicated runtime layer and always render above Fast Travel, bases, and other fixed POIs.
- Player icons are offset slightly above their exact coordinate so a co-located Fast Travel marker remains discoverable.
- Player position refresh supports **1 / 2 / 3 / 5 / 10 / 15 / 30 seconds**, defaults to **3 seconds**, and is persisted in browser storage.
- Guild bases and custom markers keep an independent **30-second** refresh schedule.
- On first entry, server entities, Fast Travel, Towers, and locale POIs are prepared in parallel; slow raster tiles do not block runtime markers.

## Included records

Each locale index contains **1,251** records with the same stable IDs and coordinates.

The Fast Travel layer now includes nine additional Palworld 1.0 records and bundles 94 markers. The UI exposes the gap against the 149 currently known upstream records so the frontend snapshot is not mistaken for a complete official mirror.

| Group | Category | Count |
|---|---|---:|
| Locations | Fast Travel | 94 |
| Locations | Dungeons | 144 |
| Locations | Region names | 13 |
| Locations | Towers | 14 |
| Locations | Quest locations | 20 |
| Locations | Special locations | 7 |
| Enemies | Field Alpha bosses | 71 |
| Enemies | Tower bosses | 7 |
| Enemies | Enemy camps | 31 |
| Enemies | Wanted targets | 32 |
| Enemies | Predator/event locations | 33 |
| Resources | Ore | 57 |
| Resources | Coal | 58 |
| Resources | Sulfur | 56 |
| Resources | Pure Quartz | 70 |
| Resources | Crude Oil | 68 |
| Resources | Skill Fruit Trees | 28 |
| Collectibles | Chests | 57 |
| Collectibles | Pal Eggs | 50 |
| Collectibles | Lifmunk Effigies | 58 |
| Collectibles | Notes | 46 |
| Collectibles | Other / treasure-map dig sites | 42 |
| NPCs | Wandering merchants | 13 |
| NPCs | Pal merchants | 10 |
| NPCs | Black Marketeers | 3 |
| NPCs | Other functional NPCs | 20 |
| Pals | General habitats | 58 |
| Pals | Rare / predator Pals | 33 |
| Pals | Daytime activity | 40 |
| Pals | Nighttime activity | 18 |
| **Total** | **30 static categories** | **1,251** |

## Restore Defaults

Restore Defaults enables exactly:

- online players;
- offline players;
- guild bases;
- custom markers;
- Fast Travel;
- Towers.

All other fixed categories remain installed and searchable but start disabled to avoid an unreadable first view.

## Localization

- `zh-CN`: Chinese primary display names; English source names are aliases/keywords only.
- `en-US`: English primary display names.
- `ja-JP`: Japanese category names with aligned stable IDs and coordinates.

The generator rejects count divergence and the frontend production gate rejects non-Chinese primary names in the Simplified Chinese index.

## Data scope and attribution

This is a curated extended offline baseline intended to make every PalOps category usable without a manual package upload. It is not described as a complete mirror of every marker available on a third-party live map. Source and license records are displayed under **About System** and maintained in `THIRD-PARTY-NOTICES.md`.

## Data maintenance and verification

The reviewed POI JSON is committed directly; no Python generator is required.

```powershell
cd frontend-vue
npm run verify:map-complete-local
npm run build
```

The generator is deterministic: rerunning it with the same inputs produces the same locale JSON, compatibility seed, and manifest hashes.

## World Tree fast travel

The local frontend index includes 15 fast-travel points inside the World Tree map bounds. `Foot of the World Tree` remains on Palpagos because its coordinate belongs to the approach area outside the World Tree tile bounds.

## Runtime marker priority

Players, guild bases, and custom markers are requested during world-map component setup. The backend parses the `types` query before snapshot/static processing and skips all fixed-map repository work for the runtime-only request.

## Save timestamps

Save-index, guild, member last-seen, player archive, and overview snapshot timestamps are formatted with the `Asia/Shanghai` time zone. New save-index lifecycle timestamps are serialized with an explicit `+08:00` offset while remaining `DateTimeOffset` values for correct absolute-time comparisons.
