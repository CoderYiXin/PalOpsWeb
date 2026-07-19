# PalDefender Configuration Management

> Language: [简体中文](paldefender-configuration-management.md) | **English**

## Purpose and boundary

The PalOps Web protection workspace manages an allowlisted set of JSON files under the confirmed `Pal/Binaries/Win64/PalDefender` directory. It is not a general filesystem editor. Paths, filenames, file types, size, JSON shape, reparse points, and concurrent changes are checked on the server before any write.

Configuration bodies and REST tokens are never written to audit or system logs.

## Supported files

The manager covers the PalDefender configuration families documented by the project, including:

- `Config.json`;
- `RESTConfig.json` and REST token files;
- `WhiteList.json` and `BanList.json`;
- Pal import rules;
- Pal templates;
- summon definitions;
- related generated examples explicitly allowlisted by the backend.

Unknown files and directories are rejected even when they are located below the PalDefender root.

## Chinese field hints and comments

The structured editor maps supported keys to localized labels and descriptions. JSON itself remains standards-compliant: explanatory text is rendered by the UI and schema metadata rather than by writing JavaScript-style comments or trailing commas into JSON files.

The editor provides:

- localized field name;
- purpose and expected type;
- default or example value;
- range/enum hints;
- warnings for sensitive or restart-dependent settings.

## REST configuration and tokens

`RESTConfig.json` can be generated from a controlled template and edited through the same validation pipeline. Real token files use cryptographically random values and are distinct from `TokenExample.json`. Example tokens must never be used as production credentials.

PalOps stores a configured REST token as a protected secret and sends it as `Authorization: Bearer <token>` only to the configured PalDefender endpoint.

## Permissions

- Viewer and Auditor: read-only access where policy permits;
- Operator: no sensitive configuration write access;
- Administrator and Owner: validate and save allowlisted files;
- Owner: sensitive runtime and deployment settings.

Backend policy is authoritative regardless of frontend visibility.

## Validation

Before saving, PalOps checks:

- valid UTF-8 JSON;
- expected root object/array type;
- known field types, ranges, enums, and required members;
- maximum file size of 2 MiB;
- filename and directory allowlist;
- no path traversal or reparse-point escape;
- caller-provided SHA-256 still matches the current file.

## Safe write sequence

```text
Read current file and SHA-256
→ validate path and JSON
→ compare optimistic concurrency hash
→ create timestamped backup
→ write a temporary file in the same directory
→ flush and parse the temporary file
→ atomically replace the target
→ re-read and return the new SHA-256
→ write metadata-only audit record
```

A stale SHA-256 returns a conflict instead of overwriting external edits.

## API behavior

The PalDefender endpoint group exposes list, read, validate, generate, save, backup, and delete operations only for supported resources. Error responses distinguish validation failure, conflict, authorization failure, missing installation, and I/O failure.

After a successful save, the UI states whether `reloadcfg` is sufficient or a PalServer restart is required.

## Smoke test

Use a non-production server to verify:

1. list and read;
2. role restrictions;
3. invalid JSON and path rejection;
4. SHA-256 conflict protection;
5. backup creation and atomic save;
6. reload/restart application;
7. restoration of the original file;
8. audit records contain no configuration body or token.
