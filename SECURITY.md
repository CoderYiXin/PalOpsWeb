# Security Policy

## Supported versions

Security fixes are applied to the latest maintained branch. Pre-release and development snapshots may change without compatibility guarantees.

## Reporting a vulnerability

Do not open a public issue for an unpatched vulnerability involving authentication, CSRF, RCON, arbitrary file access, credential exposure, save restoration, process control, Webhook SSRF, command injection, or remote code execution.

Use a private GitHub security advisory or another private channel published by the repository maintainer. Include:

- affected commit or version;
- deployment environment;
- minimal reproduction steps;
- security impact;
- sanitized logs and trace/request ID;
- suggested mitigation, when known.

Do not include live credentials, Data Protection keys, account stores, real player saves, or unredacted operational data.

## Supported deployment model

The process-management feature is supported only when PalOps Web and PalServer run on the same Windows host. Do not expose PalOps Web directly to the public Internet. Use a trusted LAN or an HTTPS reverse proxy with strict access controls.

Palworld REST, PalDefender REST, RCON, and PalOps Web management ports must not be exposed unauthenticated. Remote agents, Linux process control, UNC save paths, and arbitrary browser-supplied shell commands are outside the supported model.

## Authentication, authorization, and CSRF

Frontend menu visibility is not authorization. Every write endpoint must enforce the authenticated session, CSRF validation, role policy, request validation, and any operation-specific confirmation on the backend.

Owner, Administrator, Operator, Auditor, and Viewer roles have intentionally different capabilities. Changes that weaken these boundaries require explicit security review. Account, role, process, restore, high-risk RCON, and sensitive notification changes must be audited.

## Process control

- Owner-only configuration changes can alter executable paths and launch arguments.
- Normal stop requires a freshly identity-verified PalServer/Shipping process and a successful RCON acknowledgement for `Shutdown 1 Server will shut down in 1 seconds`.
- After the RCON acknowledgement, PalOps publishes a live ten-second grace countdown. If the installation has not fully exited, it may terminate only the originally locked PID and freshly identity-verified residual `PalServer.exe`/Shipping processes belonging to the configured installation.
- If RCON does not acknowledge the shutdown command, or if the process identity changes before cleanup, automatic residual cleanup is cancelled and the operation fails safely.
- The independent Force Stop action can corrupt or lose save data and is Owner-only with typed confirmation and auditing.
- `IdentityUnknown` intentionally blocks stop actions when the executable path cannot be verified.
- Runtime management accepts only confirmed Windows-local paths and a verified `PalServer.exe` identity.
- The browser cannot send an arbitrary one-off shell command for execution.

Validate lifecycle changes on a non-production server before using them on a live community server.

## Webhook and SSRF risk

Webhook destinations are outbound network access. Keep private-network destinations disabled unless operationally required. The application validates schemes, host resolution, redirects, payload size, and response size, but administrators remain responsible for the destinations they authorize.

The final provider-generated URL is validated, not only the value initially entered by an administrator. Do not weaken the destination validator, redirect policy, timeout limits, body limits, response limits, or URL redaction without a dedicated threat review. A successful primary operation must not be rolled back because a Webhook fails.

## Secrets and logs

ASP.NET Core Data Protection keys are required to decrypt saved Webhook and connection secrets after an upgrade or migration. Back up the key ring using access controls equivalent to the protected credentials. Copying encrypted configuration without the matching key ring can make the values unrecoverable.

Logs, screenshots, bug reports, and GitHub Actions artifacts must never contain:

- passwords, RCON credentials, tokens, cookies, or authorization headers;
- Data Protection key files or local account databases;
- signed Webhook URLs or Bot API paths containing a token;
- live save files, backups, save indexes, guild/player identifiers, or IP addresses;
- launch command lines containing secrets;
- production filesystem paths or user profile names when they are not required for diagnosis.

Use fabricated names, reserved example domains, and sanitized traces in public reports.

## PalDefender configuration files

The PalDefender configuration manager is a local-file administration feature and must not be exposed as an arbitrary file browser.

- The root is derived from the configured local PalServer executable/script/working directory and resolves only to `Pal/Binaries/Win64/PalDefender`.
- Only `Config.json`, `WhiteList.json`, `Banlist.json`, and direct `.json` children of `Pals/ImportRules`, `Pals/Templates`, and `Pals/Summons` are allowed.
- Absolute paths, drive-qualified paths, `..`, unknown directories, non-JSON files, symbolic links, and Windows reparse points are rejected server-side.
- Reads require authentication. Validation, create, update, and delete require the Administrator policy and CSRF validation.
- Saves are limited to 2 MiB, validated again on the server, protected by an expected SHA-256 value, backed up, flushed to disk, and atomically replaced.
- Audit events contain the relative path, kind, size, actor, and outcome; they must not contain full configuration contents, tokens, or secrets.
- The PalOps service account should receive only the minimum read/write permissions required for the PalDefender configuration root and `.palops-backups`.

See [`docs/paldefender-configuration-management.md`](docs/paldefender-configuration-management.md) and [`docs/paldefender-deployment.md`](docs/paldefender-deployment.md).

## Reverse proxy and SignalR

A reverse proxy must preserve WebSocket upgrade headers, forward the original scheme correctly, and enforce HTTPS. Misconfigured forwarded headers or public unauthenticated exposure is outside the supported security model.

SignalR at `/hubs/palops` uses the authenticated session for state and event delivery. It is not a mutation channel. Process, backup, player, RCON, notification, and configuration changes must continue to use CSRF-protected HTTP endpoints.

## Backup restoration

Restore is disabled by default. It requires administrator authorization, CSRF validation, an enabled setting, a stopped-server marker file, a valid backup, `CONFIRM`, the exact backup filename, and a pre-restore backup. Operators should validate the procedure on a non-production server and restrict filesystem write permissions outside maintenance windows.

Backup files are untrusted archives. Path-containment, manifest, size, and hash validation must remain in place. The live world directory must not be nested inside the backup destination or vice versa.

## Save parsing and privacy

Save parsing is read-only and operates on a private snapshot. Input/output size, decompression ratio, recursion, property size, collection size, and path boundaries are security controls, not optional performance tuning.

Do not attach real unredacted saves to public issues. Arrange private transfer only after a maintainer requests it and remove player names, platform identifiers, IP addresses, guild names, local paths, and credentials wherever possible.

## Offline maps and third-party data

Offline map tiles, POIs, icons, and other game-derived assets may have redistribution restrictions independent of the source-code license. Release maintainers must verify authorization before publishing them. A source release must not claim ownership of Pocketpair assets or community map imagery.

Version 1.1.0 does not accept map ZIP uploads and does not expose backend import, signature, activation, rollback, or tile-health endpoints. Fixed tiles, POIs, icons, bounds, and coordinate transforms are frontend release artifacts. Release maintainers must verify their hashes, provenance, and redistribution rights before publication. Do not reintroduce arbitrary archive extraction or server-side map-package management without a dedicated threat model and security review.
