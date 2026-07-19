> Language: [简体中文](README.md) | **English**

# Documentation

This directory contains the current operator, contributor, architecture, and data-reference documentation for PalOps Web. Historical design drafts, implementation plans, one-off defect reports, and packaging audit reports are intentionally excluded from the public source tree.

## Operations

- [Deployment guide](deployment.md) — supported Windows topology, first deployment, upgrade, restore, reverse proxy, polling, and release verification.
- [PalDefender deployment](paldefender-deployment.md) — installation, REST API, token, firewall, upgrade, rollback, and troubleshooting.
- [PalDefender configuration management](paldefender-configuration-management.md) — supported JSON files, validation, backups, concurrency control, and reload behavior.
- [Release checklist](release-checklist.md) — source, build, runtime, map, PalDefender, and archive acceptance checks.

## Development

- [Architecture](architecture.md) — application boundaries, authentication, runtime control, save parsing, map rendering, persistence, and observability.
- [Build guide](build.md) — required toolchain, canonical build commands, frontend generation, map data generation, and publication.
- [Frontend guide](../frontend-vue/README.md) — Vue source layout, UI conventions, API contracts, map lifecycle, and validation commands.

## Data and third-party integration

- [Frontend static map bundle](map-data-package.md) — frontend-only map resources and removed backend map-package behavior.
- [World map data baseline](world-map-data-1.1.0.md) — current POI, category, tile, attribution, and update baseline.
- [Paldeck data sources](paldeck-data-sources.md) — offline item/Pal catalog sources, placeholders, and synchronization rules.
- [Documentation images](images/README.md) — screenshot dimensions, fictional demonstration values, and sanitization requirements.
- [Third-party notices](../THIRD-PARTY-NOTICES.md) — dependency, asset, license, and attribution records.

## Repository policies

- [Contributing](../CONTRIBUTING.md)
- [Security policy](../SECURITY.md)
- [Changelog](../CHANGELOG.md)
