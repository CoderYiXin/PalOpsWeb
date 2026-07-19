# Paldeck Offline Catalog Data

> Language: [简体中文](paldeck-data-sources.md) | **English**

## Sources

PalOps Web ships offline item and Pal catalogs so routine save parsing and resource selection do not depend on a third-party service at runtime. The maintained source records are collected from the documented Paldeck pages and normalized into repository-controlled JSON.

The source layer retains:

- internal stable ID;
- English source name;
- Simplified Chinese display name where available;
- category and detail-page URL;
- static image reference where redistribution is permitted;
- provenance and synchronization metadata.

## Built-in catalogs

The release includes the catalog types needed by the UI and save projection, including items, Pals, passive skills, active skills, structures, and technologies. Unknown IDs remain visible through deterministic placeholders instead of being silently dropped.

## Six-category synchronization tool

`tools/paldeck-sync/` is a maintenance-time tool. It fetches the six supported Paldeck groups, merges curated Chinese names, validates stable IDs, and writes deterministic offline output. It is not started by PalOps Web and is not required on a production server.

See [`../tools/paldeck-sync/README.md`](../tools/paldeck-sync/README.md) for commands and maintenance rules.

## Update restrictions

- Do not replace stable IDs because a display name changed.
- Do not overwrite curated Chinese names with untranslated source text.
- Keep source attribution and applicable license records current.
- Run catalog verification and the full frontend build after any update.
- Treat missing or ambiguous data as an explicit placeholder and record the reason.
