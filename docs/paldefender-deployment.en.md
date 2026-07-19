# PalDefender Deployment and PalOps Web Integration

> Language: [简体中文](paldefender-deployment.md) | **English**

## 1. Scope

This guide covers a Windows-local installation where PalDefender runs inside the same Palworld dedicated-server installation managed by PalOps Web. It explains file placement, REST configuration, token creation, firewall boundaries, PalOps integration, upgrades, rollback, and troubleshooting.

PalDefender and PalOps Web are separate applications. PalOps connects through PalDefender REST and manages only allowlisted files under the confirmed PalDefender directory.

## 2. Preparation

Before changing the server:

- stop PalServer normally;
- back up the world save;
- back up `Pal/Binaries/Win64` and any existing PalDefender directory;
- record the current PalServer and PalDefender versions;
- confirm no unrelated software already uses the intended REST port;
- choose a strong random REST token;
- decide whether REST will bind only to loopback or to a trusted management LAN.

## 3. Install PalDefender

Place the PalDefender loader and core files in the locations required by the PalDefender release. The core binary is loaded from the Palworld server Win64 directory, commonly under:

```text
<PalServerRoot>\Pal\Binaries\Win64\
```

After startup, confirm the console reports that PalDefender loaded and that its configuration directory was created. Do not continue with REST integration until the server starts normally with PalDefender enabled.

## 4. Understand the two REST APIs

PalOps may use two independent upstreams:

- **Palworld REST API**: official server information and metrics when enabled;
- **PalDefender REST API**: PalDefender-specific version, players, metrics, and protection functions.

They can use different ports, credentials, paths, and availability states. A successful Palworld REST connection does not prove PalDefender REST is configured.

## 5. Configure PalDefender REST

### 5.1 `RESTConfig.json`

Generate or edit the REST configuration using valid JSON. Bind to loopback whenever PalOps runs on the same host. Use a trusted LAN bind only when there is a clear operational requirement and a matching firewall rule.

Avoid exposing PalDefender REST directly to the public internet.

### 5.2 Create a real token

Create the actual token file required by PalDefender. The token must be a strong random value. `TokenExample.json` is documentation only and must not be used as the production credential.

PalOps sends the token as:

```http
Authorization: Bearer <token>
```

Store it only in PalOps protected settings and the PalDefender token file. Never commit it to Git or include it in screenshots, logs, issues, or release archives.

## 6. Configure PalOps Web

In the protection workspace or system settings:

1. enter the PalDefender REST base URL;
2. enter the real Bearer token;
3. test connectivity;
4. verify current version and player/metric endpoints;
5. confirm the resolved PalDefender configuration directory;
6. open supported files in read-only mode first;
7. test validation, backup, and save on a non-production change.

Use `127.0.0.1` when both applications run on the same host unless IPv6 or a specific interface is required.

## 7. Windows Firewall and network boundary

- allow only the PalOps web port to intended administrators;
- keep RCON, Palworld REST, and PalDefender REST on loopback or a trusted management subnet;
- do not publish those upstream ports through a consumer router;
- if a reverse proxy is used, expose PalOps through HTTPS and preserve SignalR WebSocket upgrades;
- restrict inbound rules by interface/profile and source subnet.

## 8. File-management permissions

The Windows account running PalOps needs read access to the PalDefender directory and write access only if online configuration management is enabled. It must be able to create `.palops-backups`, temporary files, and atomic replacements in the same directory.

Do not grant broad write permission to the entire PalServer installation when a narrower ACL is sufficient.

## 9. Upgrade PalDefender

1. read the PalDefender release notes;
2. stop PalServer normally;
3. back up the current binaries, configuration, token files, and `.palops-backups`;
4. replace only files required by the new release;
5. retain configuration unless the release requires migration;
6. start PalServer and inspect the PalDefender console output;
7. test REST, token authorization, players/metrics, and PalOps configuration reads;
8. perform a non-production config validation/save test.

## 10. Rollback

Stop PalServer, restore the backed-up PalDefender binaries and compatible configuration, then start the server and verify console load plus REST authorization. Do not mix an older binary with a configuration format introduced by a newer version unless PalDefender documents compatibility.

## 11. Troubleshooting

### Configuration directory is not created

Verify file placement, architecture, loader requirements, Windows file blocking, permissions, and PalServer console errors.

### REST returns 401

The Bearer token is missing, malformed, read from the example file, or different from the active PalDefender token.

### REST returns 403

The token is valid but lacks the required permission, or the requested operation is disabled by PalDefender policy.

### Connection refused or timeout

Check the REST bind address, port, process startup log, firewall, URL scheme, and whether another program owns the port.

### PalOps can read but cannot save

Check the PalOps service-account ACL, file read-only flags, antivirus interference, reparse-point protection, concurrent SHA-256 conflict, and the `.palops-backups` directory.

### Console or log encoding issues

Use a UTF-8 capable terminal and keep JSON files encoded as UTF-8 without invalid bytes.

## 12. Post-deployment checklist

- PalDefender loads without fatal errors;
- the real token authenticates and the example token does not;
- REST is not publicly exposed;
- PalOps version/player/metric checks work;
- allowlisted configuration files can be read;
- validation, backup, atomic save, and reload/restart behavior are verified;
- secrets are absent from console, system logs, audit logs, screenshots, and Git history;
- rollback files are retained until the new version is accepted.

## 13. References

Use the official PalDefender documentation and release notes as the authority for file placement, supported configuration keys, REST behavior, and version-specific migration requirements.
