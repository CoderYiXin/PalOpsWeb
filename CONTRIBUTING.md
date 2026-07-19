# Contributing to PalOps Web

## Change scope

- Use an issue for significant features, save-format changes, security-sensitive behavior, new outbound integrations, or process-control changes.
- Keep changes focused and avoid unrelated refactoring.
- Do not add a runtime PST, Python, Go, or external save-parser dependency.
- Save parsing remains read-only; management changes must use documented REST or RCON channels.
- SignalR is state/event delivery only. Mutations use authenticated, CSRF-protected HTTP endpoints.
- This repository intentionally does not contain unit-test projects or unit-test scripts. Do not reintroduce them unless the maintenance policy is changed explicitly.

## Branches and commits

Suggested branch names:

```text
feature/<short-name>
fix/<short-name>
docs/<short-name>
save-format/<palworld-version>
```

Use focused commit messages such as `feat: add guild-base projection`, `fix: reject an unknown PalServer identity`, or `docs: clarify release verification`.

## Required validation

Every pull request that changes runtime code, frontend code, documentation, build scripts, or dependencies must pass:

```powershell
.\scripts\build.ps1
```

The script is the canonical local validation entry point. It performs frontend dependency installation, strict type checking, production compilation, backend restore/build, and repository tooling checks defined by the current release workflow.

Changes affecting process lifecycle, save projection, map placement, SignalR, Webhooks, permissions, backup/restore, or migrations must also document the matching manual smoke scenario in the pull request description. Include the environment, safe synthetic inputs, expected result, actual result, and sanitized trace/request ID where available.

Before merging server-management changes, run the smoke scenario on a non-production Windows server. Never use a production server as the first validation target.

A distributable offline-map build must contain complete, authorized Palpagos and World Tree tile sets and pass strict map verification. `--allow-missing` is suitable only for source-development checks, not release packaging.

## Prohibited fixtures and artifacts

Do not commit real `.sav`, database, Data Protection key, cookie, token, password, signed Webhook URL, user profile path, Steam/EOS identifier, or production screenshot. Use fabricated fixtures and reserved example addresses.

The prohibited set includes:

- `Level.sav` and player `.sav` files containing real users;
- backups, save indexes, audit logs, system logs, runtime operation histories, and Webhook histories;
- local account stores and password hashes;
- Data Protection XML key rings;
- IP addresses, player/guild names, platform identifiers, or private map markers;
- production filesystem paths or Windows usernames;
- browser storage exports containing session or release-dismissal state.

Private compatibility samples must remain outside the repository under operator-controlled storage. Only synthetic, explicitly redistributable, or irreversibly anonymized fixtures may be published.

## C# guidelines

- Enable nullable analysis and propagate cancellation tokens.
- Keep endpoint handlers thin; filesystem, process, RCON, save, and outbound HTTP behavior belongs behind interfaces.
- Bound binary reads, decompression output, recursion, collection sizes, HTTP payloads, redirects, response bodies, and user-controlled paths.
- Preserve the previous successful save index when parsing or projection fails.
- Do not treat transport success as business success.
- A completed external side effect must not become HTTP 500 because audit, notification, or cache persistence failed afterward.
- Keep credentials, protected values, command-line secrets, and sensitive player fields out of logs and API responses.
- Process mutations must preserve the confirmed executable-identity and operation-lock rules.
- Webhook changes must revalidate the final provider-generated destination and maintain SSRF controls.
- PalDefender configuration changes must preserve the explicit path allowlist, reparse-point checks, size limit, server-side validation, SHA-256 concurrency token, backup, write-through temporary file, atomic replacement, CSRF policy, and sanitized audit metadata.

## TypeScript and Vue guidelines

- Use strict TypeScript and versioned API contracts.
- Use the authenticated HTTP client for mutations and preserve CSRF behavior.
- Keep automatic refresh non-destructive: preserve selection, expanded rows, filters, pagination, map viewport, and form input.
- SignalR handlers update state; they do not initiate privileged server mutations.
- Add every user-facing string to Simplified Chinese, English, and Japanese locale files.
- High-risk actions must use shared confirmation behavior and be authorized again by the backend.
- Use design tokens instead of hard-coded theme colors where a semantic token exists.
- Keep `/map` inside `WorkbenchShell`; responsive map panels own their internal scroll and resize lifecycles.
- Edit `frontend-vue/`; do not hand-edit hashed files under `src/PalOps.Web/wwwroot/assets`.
- PalDefender configuration editors must keep raw/structured views on the same draft, surface backend diagnostics, and never persist configuration contents or hashes in browser storage.

## Dependency changes

- Pin direct npm dependencies to exact versions.
- Update `frontend-vue/package-lock.json` with `npm install --save-exact`.
- Add or update `THIRD-PARTY-NOTICES.md` and `licenses/` when licensing requires it.
- Do not add a dependency for behavior already provided by .NET, Vue, Element Plus, MapLibre GL JS, or existing project utilities without explaining the maintenance benefit.
- Record the security, bundle-size, platform, and licensing impact in the pull request.
- Run `npm audit --audit-level=high` after frontend dependency changes.

## Save-format changes

A save-format contribution must state:

- the Palworld version and envelope type;
- the exact logical property path or RawData domain affected;
- parser limits exercised;
- how a failed parse preserves the previous index;
- whether the change affects player, container, guild, base, or coordinate reconciliation.

Do not include the real save in the pull request. Use a minimal synthetic representation or private maintainer transfer.

## Catalog and translation changes

Every item, Pal, technology, structure, passive, skill, image, map, or translation update must record its source and redistribution status. Do not replace a verified Chinese, English, or Japanese name with unchecked machine translation.

All three locale sets must remain functionally aligned. Navigation labels should preserve the compact desktop information architecture.

## Documentation and screenshots

- Keep README feature claims aligned across Chinese, English, and Japanese.
- Use only fabricated server, user, guild, Webhook, path, and coordinate data in screenshots.
- Use reserved domains such as `example.invalid` for network examples.
- Do not publish a map screenshot or tile archive unless redistribution is authorized.
- Update build, deployment, architecture, security, and release-checklist documentation when behavior or commands change.
