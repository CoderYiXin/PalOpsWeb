using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PalOps.Web.Audit;
using PalOps.Web.ServerRuntime;
using PalOps.Web.Versioning;

namespace PalOps.Web.PluginManagement;

public interface IPluginPackageService
{
    Task<PluginManagementDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<PluginManagementDashboard> CheckUpdatesAsync(string userName, string remoteIp, CancellationToken cancellationToken = default);
    Task<PluginOperationResult> InstallAsync(Stream archive, string fileName, long length, bool acknowledgeCompatibilityRisk, bool overwriteExisting, string userName, string remoteIp, CancellationToken cancellationToken = default);
    Task<PluginOperationResult> ToggleAsync(string packageId, bool enabled, string userName, string remoteIp, CancellationToken cancellationToken = default);
    Task<PluginOperationResult> RollbackAsync(string backupId, string userName, string remoteIp, CancellationToken cancellationToken = default);
}

public sealed partial class PluginPackageService(
    IPluginManagementPathResolver pathResolver,
    IPluginManagementRepository repository,
    IPluginInventoryScanner scanner,
    IPluginReleaseClient releaseClient,
    IPalServerRuntimeCoordinator runtimeCoordinator,
    IAuditLogService audit,
    ILogger<PluginPackageService> logger) : IPluginPackageService
{
    private const long MaximumArchiveBytes = 256L * 1024 * 1024;
    private const long MaximumExtractedBytes = 1024L * 1024 * 1024;
    private const int MaximumEntries = 20_000;
    private const int MaximumManifestBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public Task<PluginManagementDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
        => scanner.ScanAsync(cancellationToken);

    public async Task<PluginManagementDashboard> CheckUpdatesAsync(string userName, string remoteIp, CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var dashboardBefore = await scanner.ScanAsync(cancellationToken);
            var state = await repository.GetAsync(cancellationToken);
            var checkedCount = 0;
            foreach (var package in dashboardBefore.Packages.OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var repositoryName = state.Packages.TryGetValue(package.Id, out var registration)
                    ? ResolveRepository(registration)
                    : ResolveKnownRepository(package.Id);
                if (string.IsNullOrWhiteSpace(repositoryName)) continue;
                checkedCount++;
                try
                {
                    var release = await releaseClient.GetLatestStableAsync(repositoryName, cancellationToken);
                    var currentParsed = SemanticVersionValue.TryParse(package.Version, out var current);
                    var latestParsed = SemanticVersionValue.TryParse(release?.TagName, out var latest);
                    state.Releases[package.Id] = new(
                        package.Id,
                        package.Version,
                        release?.TagName ?? string.Empty,
                        release is not null,
                        currentParsed && latestParsed,
                        currentParsed && latestParsed && latest.CompareTo(current) > 0,
                        release?.HtmlUrl ?? string.Empty,
                        release?.Name ?? string.Empty,
                        release?.Body ?? string.Empty,
                        release?.PublishedAt,
                        DateTimeOffset.UtcNow,
                        release is null ? "GitHub 未返回稳定版 Release。" : null);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Plugin release check timed out for {PluginId}.", package.Id);
                    state.Releases[package.Id] = new(
                        package.Id, package.Version, string.Empty, false, false, false,
                        string.Empty, string.Empty, string.Empty, null, DateTimeOffset.UtcNow, "GitHub 更新检查超时。");
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or PluginManagementException)
                {
                    logger.LogWarning(ex, "Unable to check plugin release for {PluginId}.", package.Id);
                    state.Releases[package.Id] = new(
                        package.Id, package.Version, string.Empty, false, false, false,
                        string.Empty, string.Empty, string.Empty, null, DateTimeOffset.UtcNow, Limit(ex.Message, 300));
                }
            }
            await repository.SaveAsync(state, cancellationToken);
            await TryWriteAuditAsync("plugin.update-check", "success", remoteIp, "已检查插件与模组更新。", new { operatorName = userName, packageCount = checkedCount }, cancellationToken);
            return await ScanAfterCommitAsync(dashboardBefore, cancellationToken);
        }
        finally { _operationGate.Release(); }
    }

    public async Task<PluginOperationResult> InstallAsync(
        Stream archive,
        string fileName,
        long length,
        bool acknowledgeCompatibilityRisk,
        bool overwriteExisting,
        string userName,
        string remoteIp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        fileName = Limit(Path.GetFileName(fileName.Replace('\\', '/')), 200);
        if (length <= 0 || length > MaximumArchiveBytes)
            throw new PluginManagementException(413, "PLUGIN_ARCHIVE_TOO_LARGE", "插件 ZIP 必须大于 0 且不超过 256 MiB。", fileName);
        if (!Path.GetExtension(fileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new PluginManagementException(422, "PLUGIN_ARCHIVE_TYPE_INVALID", "仅支持 ZIP 插件包。", fileName);

        await _operationGate.WaitAsync(cancellationToken);
        var operationId = "plugin-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var startedAt = DateTimeOffset.UtcNow;
        PluginPackageManifest? manifest = null;
        PluginManagementPaths? paths = null;
        string? staging = null;
        string? uploaded = null;
        PluginBackupRecord? backup = null;
        bool backupRecorded = false;
        bool deploymentApplied = false;
        bool automaticRollbackCompleted = false;
        bool operationCommitted = false;
        try
        {
            paths = await pathResolver.ResolveAsync(cancellationToken);
            await EnsureServerStoppedAsync(cancellationToken);
            staging = Path.Combine(paths.StagingRoot, operationId);
            Directory.CreateDirectory(staging);
            uploaded = Path.Combine(staging, "package.zip");
            await CopyBoundedAsync(archive, uploaded, MaximumArchiveBytes, cancellationToken);
            var archiveSha256 = await HashFileAsync(uploaded, cancellationToken);
            PackageExtraction extraction;
            try
            {
                extraction = await ExtractPackageAsync(uploaded, Path.Combine(staging, "content"), cancellationToken);
            }
            catch (PluginManagementException) { throw; }
            catch (Exception ex) when (ex is InvalidDataException or JsonException)
            {
                throw new PluginManagementException(422, "PLUGIN_ARCHIVE_INVALID", "插件 ZIP 或 palops-package.json 格式无效。", Limit(ex.Message, 500), innerException: ex);
            }
            manifest = ValidateManifest(extraction.Manifest);
            var installRoot = pathResolver.ResolveInstallDirectory(paths, manifest.InstallDirectory, manifest.Kind);
            var dashboard = await scanner.ScanAsync(cancellationToken);
            ValidateCompatibility(manifest, dashboard.GameVersion, acknowledgeCompatibilityRisk);
            ValidateDependencies(manifest, dashboard.Packages);

            var state = await repository.GetAsync(cancellationToken);
            state.Packages.TryGetValue(manifest.Id, out var previous);
            ValidateDependencyGraph(manifest, state.Packages);
            ValidateEnabledDependentsForVersion(manifest.Id, manifest.Version, state.Packages);
            var installedFiles = extraction.Files
                .Select(relative => pathResolver.ToServerRelativePath(paths, Path.Combine(installRoot, relative.Replace('/', Path.DirectorySeparatorChar))))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (installedFiles.Length == 0)
                throw new PluginManagementException(422, "PLUGIN_ARCHIVE_EMPTY", "插件包没有可安装文件。");
            EnsureOverwriteAllowed(paths, previous, installedFiles, overwriteExisting);

            var affected = (previous?.InstalledFiles ?? Array.Empty<string>())
                .Concat(installedFiles)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            backup = await CreateBackupAsync(paths, manifest, previous, affected, userName, cancellationToken);
            state.Backups.Insert(0, backup);
            var expiredBackups = ApplyBackupRetention(state);
            await repository.SaveAsync(state, cancellationToken);
            backupRecorded = true;
            DeleteExpiredBackups(paths, expiredBackups);

            var enabled = previous?.Enabled ?? true;
            try
            {
                await DeployAsync(paths, extraction.ContentRoot, installRoot, affected, installedFiles, enabled, operationId, cancellationToken);
                deploymentApplied = true;
            }
            catch (Exception deploymentError)
            {
                try
                {
                    await RestoreBackupFilesAsync(paths, backup, affected, CancellationToken.None);
                    automaticRollbackCompleted = true;
                    await MarkBackupRestoredAsync(backup.BackupId, "system-auto-rollback", CancellationToken.None);
                }
                catch (Exception rollbackError)
                {
                    throw new PluginManagementException(
                        500,
                        "PLUGIN_INSTALL_ROLLBACK_FAILED",
                        "插件安装失败，且自动回滚未完成。请立即检查插件目录和备份。",
                        deploymentError.Message,
                        $"备份文件：{backup.ArchiveFileName}；回滚错误：{rollbackError.Message}",
                        new AggregateException(deploymentError, rollbackError));
                }
                throw;
            }

            var now = DateTimeOffset.UtcNow;
            var registration = new ManagedPluginRegistration(
                manifest.Id,
                manifest.Name,
                manifest.Kind,
                manifest.Version,
                manifest.InstallDirectory,
                manifest.EntryPaths,
                installedFiles,
                manifest.Dependencies,
                manifest.CompatibleGameVersions,
                ResolveRepository(manifest),
                archiveSha256,
                enabled,
                previous?.InstalledAt ?? now,
                now,
                NormalizeUser(userName));
            state = await repository.GetAsync(cancellationToken);
            state.Packages[manifest.Id] = registration;
            state.Releases.Remove(manifest.Id);
            var operation = new PluginOperationRecord(
                operationId,
                previous is null ? "install" : "update",
                manifest.Id,
                manifest.Name,
                "success",
                NormalizeUser(userName),
                NormalizeIp(remoteIp),
                startedAt,
                DateTimeOffset.UtcNow,
                previous is null ? $"已安装 {manifest.Name} {manifest.Version}。" : $"已将 {manifest.Name} 更新到 {manifest.Version}。",
                null);
            state.History.Insert(0, operation);
            await repository.SaveAsync(state, cancellationToken);
            operationCommitted = true;
            await TryWriteAuditAsync("plugin." + operation.Operation, "success", remoteIp, operation.Summary, new { operationId, manifest.Id, manifest.Version, backup.BackupId, archiveSha256 }, cancellationToken);
            var finalDashboard = await ScanAfterCommitAsync(dashboard, cancellationToken);
            return new(operationId, manifest.Id, operation.Operation, "success", operation.Summary, finalDashboard);
        }
        catch (Exception ex)
        {
            Exception failure = ex;
            if (deploymentApplied && !operationCommitted && !automaticRollbackCompleted && paths is not null && backup is not null)
            {
                try
                {
                    await RestoreBackupFilesAsync(paths, backup, backup.AffectedFiles, CancellationToken.None);
                    automaticRollbackCompleted = true;
                    await MarkBackupRestoredAsync(backup.BackupId, "system-auto-rollback", CancellationToken.None);
                }
                catch (Exception rollbackError)
                {
                    failure = new PluginManagementException(
                        500,
                        "PLUGIN_INSTALL_ROLLBACK_FAILED",
                        "插件安装失败，且自动回滚未完成。请立即检查插件目录和备份。",
                        ex.Message,
                        $"备份文件：{backup.ArchiveFileName}；回滚错误：{rollbackError.Message}",
                        new AggregateException(ex, rollbackError));
                }
            }

            if (backup is not null && paths is not null && !backupRecorded)
            {
                try { TryDeleteFile(ResolveBackupArchivePath(paths, backup.ArchiveFileName)); }
                catch (Exception cleanupError) { logger.LogWarning(cleanupError, "Unable to remove orphan plugin backup {BackupId}.", backup.BackupId); }
            }

            var packageId = manifest?.Id ?? "unknown";
            var packageName = manifest?.Name ?? Path.GetFileName(fileName);
            try
            {
                var state = await repository.GetAsync(CancellationToken.None);
                state.History.Insert(0, new(
                    operationId,
                    manifest is null ? "install" : state.Packages.ContainsKey(packageId) ? "update" : "install",
                    packageId,
                    packageName,
                    "failed",
                    NormalizeUser(userName),
                    NormalizeIp(remoteIp),
                    startedAt,
                    DateTimeOffset.UtcNow,
                    automaticRollbackCompleted ? "插件安装或更新失败，文件已自动回滚。" : "插件安装或更新失败。",
                    Limit(failure.Message, 500)));
                await repository.SaveAsync(state, CancellationToken.None);
                await audit.WriteAsync("plugin.install", "failed", remoteIp, "插件安装或更新失败。", new { operationId, packageId, error = Limit(failure.Message, 500), backupId = backup?.BackupId, automaticRollbackCompleted }, CancellationToken.None);
            }
            catch (Exception recordError) { logger.LogError(recordError, "Unable to record failed plugin installation {OperationId}.", operationId); }
            if (!ReferenceEquals(failure, ex)) throw failure;
            throw;
        }
        finally
        {
            if (staging is not null) TryDeleteDirectory(staging);
            _operationGate.Release();
        }
    }

    public async Task<PluginOperationResult> ToggleAsync(
        string packageId,
        bool enabled,
        string userName,
        string remoteIp,
        CancellationToken cancellationToken = default)
    {
        packageId = NormalizeId(packageId);
        await _operationGate.WaitAsync(cancellationToken);
        var operationId = "toggle-" + Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var paths = await pathResolver.ResolveAsync(cancellationToken);
            await EnsureServerStoppedAsync(cancellationToken);
            var state = await repository.GetAsync(cancellationToken);
            if (!state.Packages.TryGetValue(packageId, out var registration))
                throw new KeyNotFoundException("受管插件不存在。");
            if (registration.Enabled == enabled)
            {
                var unchanged = await scanner.ScanAsync(cancellationToken);
                return new(operationId, packageId, "toggle", "success", enabled ? "插件已经启用。" : "插件已经禁用。", unchanged);
            }
            var dashboard = await scanner.ScanAsync(cancellationToken);
            if (enabled) ValidateDependencies(
                new(1, registration.Id, registration.Name, registration.Kind, registration.Version, registration.InstallDirectory, registration.EntryPaths, registration.Dependencies, registration.CompatibleGameVersions, registration.Repository, null),
                dashboard.Packages);
            else ValidateNoEnabledDependents(packageId, state);

            var moved = new List<(string Source, string Destination)>();
            var stateCommitted = false;
            try
            {
                foreach (var relative in registration.InstalledFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var canonical = pathResolver.ResolveServerRelativePath(paths, relative);
                    var disabled = canonical + ".palops-disabled";
                    var source = enabled ? disabled : canonical;
                    var destination = enabled ? canonical : disabled;
                    pathResolver.EnsureSafeExistingPath(source);
                    pathResolver.EnsureSafeExistingPath(destination);
                    if (!File.Exists(source))
                        throw new PluginManagementException(409, "PLUGIN_TOGGLE_SOURCE_MISSING", "插件文件状态不完整，无法切换。", source);
                    if (File.Exists(destination))
                        throw new PluginManagementException(409, "PLUGIN_TOGGLE_CONFLICT", "启用和禁用版本同时存在，无法安全切换。", destination);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(source, destination);
                    moved.Add((source, destination));
                }

                var now = DateTimeOffset.UtcNow;
                state.Packages[packageId] = registration with { Enabled = enabled, UpdatedAt = now, UpdatedBy = NormalizeUser(userName) };
                var summary = enabled ? $"已启用 {registration.Name}。" : $"已禁用 {registration.Name}。";
                state.History.Insert(0, new(operationId, "toggle", packageId, registration.Name, "success", NormalizeUser(userName), NormalizeIp(remoteIp), startedAt, now, summary, null));
                await repository.SaveAsync(state, CancellationToken.None);
                stateCommitted = true;
                await TryWriteAuditAsync("plugin.toggle", "success", remoteIp, summary, new { operationId, packageId, enabled }, cancellationToken);
                return new(operationId, packageId, "toggle", "success", summary, await ScanAfterCommitAsync(dashboard, cancellationToken));
            }
            catch (Exception mutationError)
            {
                if (!stateCommitted && moved.Count > 0)
                {
                    var recoveryErrors = RollbackMovedFiles(
                        Array.Empty<string>(),
                        moved.Select(static item => (Temporary: item.Destination, Original: item.Source)).ToArray());
                    if (recoveryErrors.Count > 0)
                    {
                        recoveryErrors.Insert(0, mutationError);
                        throw new PluginManagementException(
                            500,
                            "PLUGIN_TOGGLE_TRANSACTION_FAILED",
                            "插件启停失败，且文件状态恢复不完整。请人工检查 .palops-disabled 文件。",
                            mutationError.Message,
                            "停止 PalServer 后检查插件入口文件及其 .palops-disabled 文件。",
                            new AggregateException(recoveryErrors));
                    }
                }
                throw;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordFailureAsync(operationId, "toggle", packageId, packageId, userName, remoteIp, startedAt, ex, CancellationToken.None);
            throw;
        }
        finally { _operationGate.Release(); }
    }

    public async Task<PluginOperationResult> RollbackAsync(
        string backupId,
        string userName,
        string remoteIp,
        CancellationToken cancellationToken = default)
    {
        backupId = NormalizeId(backupId);
        await _operationGate.WaitAsync(cancellationToken);
        var operationId = "rollback-" + Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        PluginManagementPaths? paths = null;
        PluginBackupRecord? safetyBackup = null;
        var preserveSafetyBackup = false;
        try
        {
            paths = await pathResolver.ResolveAsync(cancellationToken);
            await EnsureServerStoppedAsync(cancellationToken);
            var state = await repository.GetAsync(cancellationToken);
            var dashboardBefore = await scanner.ScanAsync(cancellationToken);
            var backup = state.Backups.FirstOrDefault(item => item.BackupId.Equals(backupId, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("插件备份不存在。");
            if (backup.Restored)
                throw new PluginManagementException(409, "PLUGIN_BACKUP_ALREADY_RESTORED", "该插件备份已经回滚过。", backupId);

            var currentRegistration = state.Packages.GetValueOrDefault(backup.PackageId);
            if (backup.PreviousRegistration is null)
                ValidateEnabledDependentsForVersion(backup.PackageId, null, state.Packages);
            else
                ValidateEnabledDependentsForVersion(backup.PackageId, backup.PreviousRegistration.Version, state.Packages);
            var currentFiles = currentRegistration?.InstalledFiles ?? Array.Empty<string>();
            var affected = currentFiles.Concat(backup.AffectedFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var safetyManifest = new PluginPackageManifest(
                1,
                backup.PackageId,
                backup.PackageName,
                currentRegistration?.Kind ?? backup.PreviousRegistration?.Kind ?? PluginPackageKind.ServerMod,
                currentRegistration?.Version ?? backup.PreviousRegistration?.Version ?? "0.0.0",
                currentRegistration?.InstallDirectory ?? backup.PreviousRegistration?.InstallDirectory ?? "Pal/Binaries/Win64/Mods/PalOpsRollbackSafety",
                currentRegistration?.EntryPaths ?? backup.PreviousRegistration?.EntryPaths ?? Array.Empty<string>(),
                currentRegistration?.Dependencies ?? backup.PreviousRegistration?.Dependencies ?? Array.Empty<PluginDependencyManifest>(),
                currentRegistration?.CompatibleGameVersions ?? backup.PreviousRegistration?.CompatibleGameVersions ?? Array.Empty<string>(),
                currentRegistration?.Repository ?? backup.PreviousRegistration?.Repository ?? string.Empty,
                "Temporary safety snapshot before manual rollback.");
            safetyBackup = await CreateBackupAsync(paths, safetyManifest, currentRegistration, affected, "system-pre-rollback", cancellationToken);

            await RestoreBackupFilesAsync(paths, backup, affected, cancellationToken);
            if (backup.PreviousRegistration is null) state.Packages.Remove(backup.PackageId);
            else state.Packages[backup.PackageId] = backup.PreviousRegistration;
            var now = DateTimeOffset.UtcNow;
            var index = state.Backups.FindIndex(item => item.BackupId.Equals(backupId, StringComparison.OrdinalIgnoreCase));
            state.Backups[index] = backup with { Restored = true, RestoredAt = now, RestoredBy = NormalizeUser(userName) };
            var summary = backup.PreviousRegistration is null
                ? $"已回滚 {backup.PackageName} 的首次安装。"
                : $"已将 {backup.PackageName} 回滚到 {backup.PreviousRegistration.Version}。";
            state.History.Insert(0, new(operationId, "rollback", backup.PackageId, backup.PackageName, "success", NormalizeUser(userName), NormalizeIp(remoteIp), startedAt, now, summary, null));
            try
            {
                await repository.SaveAsync(state, CancellationToken.None);
            }
            catch (Exception commitError)
            {
                try
                {
                    await RestoreBackupFilesAsync(paths, safetyBackup, affected, CancellationToken.None);
                }
                catch (Exception compensationError)
                {
                    preserveSafetyBackup = true;
                    throw new PluginManagementException(
                        500,
                        "PLUGIN_ROLLBACK_STATE_COMMIT_FAILED",
                        "插件文件已回滚，但状态保存失败，且当前版本补偿恢复未完成。请立即人工检查。",
                        commitError.Message,
                        $"安全备份文件：{safetyBackup.ArchiveFileName}；补偿错误：{compensationError.Message}",
                        new AggregateException(commitError, compensationError));
                }
                throw new PluginManagementException(
                    500,
                    "PLUGIN_ROLLBACK_STATE_COMMIT_FAILED",
                    "插件回滚状态保存失败，文件已恢复到操作前版本。",
                    commitError.Message,
                    innerException: commitError);
            }

            await TryWriteAuditAsync("plugin.rollback", "success", remoteIp, summary, new { operationId, backupId, backup.PackageId }, cancellationToken);
            return new(operationId, backup.PackageId, "rollback", "success", summary, await ScanAfterCommitAsync(dashboardBefore, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordFailureAsync(operationId, "rollback", backupId, backupId, userName, remoteIp, startedAt, ex, CancellationToken.None);
            throw;
        }
        finally
        {
            if (paths is not null && safetyBackup is not null && !preserveSafetyBackup)
            {
                try { TryDeleteFile(ResolveBackupArchivePath(paths, safetyBackup.ArchiveFileName)); }
                catch (Exception cleanupError) { logger.LogWarning(cleanupError, "Unable to delete temporary pre-rollback safety backup {BackupId}.", safetyBackup.BackupId); }
            }
            _operationGate.Release();
        }
    }

    public static PluginPackageManifest ValidateManifest(PluginPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != 1)
            throw new PluginManagementException(422, "PLUGIN_MANIFEST_VERSION_UNSUPPORTED", "palops-package.json schemaVersion 必须为 1。");
        var id = NormalizeId(manifest.Id);
        var name = NormalizeText(manifest.Name, "插件名称", 100);
        var version = NormalizeText(manifest.Version, "插件版本", 80);
        if (!SemanticVersionValue.TryParse(version, out _))
            throw new PluginManagementException(422, "PLUGIN_VERSION_INVALID", "插件版本必须是可比较的数字版本。", version);
        var install = PluginManagementPathResolver.NormalizeRelativePath(manifest.InstallDirectory, "安装目录");
        EnsureNotReservedManagedPath(install, "安装目录");
        if (manifest.EntryPaths is null || manifest.EntryPaths.Count is < 1 or > 1000)
            throw new PluginManagementException(422, "PLUGIN_ENTRY_PATHS_INVALID", "entryPaths 必须包含 1 到 1000 个入口文件。");
        var entries = manifest.EntryPaths.Select(path => PluginManagementPathResolver.NormalizeRelativePath(path, "入口路径"))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var entry in entries) EnsureNotReservedManagedPath(entry, "入口路径");
        if (entries.Any(path => path.Equals("palops-package.json", StringComparison.OrdinalIgnoreCase)))
            throw new PluginManagementException(422, "PLUGIN_ENTRY_PATHS_INVALID", "palops-package.json 不能作为插件入口文件。");
        var dependencies = (manifest.Dependencies ?? Array.Empty<PluginDependencyManifest>())
            .Select(dependency => new PluginDependencyManifest(
                NormalizeId(dependency.PackageId),
                string.IsNullOrWhiteSpace(dependency.MinimumVersion) ? string.Empty : NormalizeText(dependency.MinimumVersion, "依赖最低版本", 80),
                dependency.Optional))
            .ToArray();
        if (dependencies.Length > 50 || dependencies.GroupBy(static item => item.PackageId, StringComparer.OrdinalIgnoreCase).Any(static group => group.Count() > 1))
            throw new PluginManagementException(422, "PLUGIN_DEPENDENCIES_INVALID", "依赖项不能超过 50 个且不能重复。");
        if (dependencies.Any(dependency => dependency.PackageId.Equals(id, StringComparison.OrdinalIgnoreCase)))
            throw new PluginManagementException(422, "PLUGIN_DEPENDENCY_CYCLE", "插件不能依赖自身。", id);
        foreach (var dependency in dependencies.Where(static item => !string.IsNullOrWhiteSpace(item.MinimumVersion)))
            if (!SemanticVersionValue.TryParse(dependency.MinimumVersion, out _))
                throw new PluginManagementException(422, "PLUGIN_DEPENDENCY_VERSION_INVALID", "依赖最低版本格式无效。", dependency.MinimumVersion);
        if ((manifest.CompatibleGameVersions?.Count ?? 0) > 50)
            throw new PluginManagementException(422, "PLUGIN_COMPATIBILITY_RULES_INVALID", "兼容游戏版本规则不能超过 50 条。");
        var compatible = (manifest.CompatibleGameVersions ?? Array.Empty<string>())
            .Select(value => NormalizeText(value, "兼容游戏版本", 50))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var repository = string.IsNullOrWhiteSpace(manifest.Repository) ? string.Empty : PluginReleaseClient.NormalizeRepository(manifest.Repository);
        if (id.Equals("paldefender", StringComparison.OrdinalIgnoreCase))
        {
            if (manifest.Kind != PluginPackageKind.PalDefender) throw new PluginManagementException(422, "PLUGIN_KIND_INVALID", "paldefender 包类型必须为 PalDefender。");
            if (string.IsNullOrWhiteSpace(repository)) repository = "Ultimeit/PalDefender";
        }
        if (id.Equals("ue4ss", StringComparison.OrdinalIgnoreCase))
        {
            if (manifest.Kind != PluginPackageKind.UE4SS) throw new PluginManagementException(422, "PLUGIN_KIND_INVALID", "ue4ss 包类型必须为 UE4SS。");
            if (string.IsNullOrWhiteSpace(repository)) repository = "UE4SS-RE/RE-UE4SS";
        }
        return manifest with
        {
            Id = id,
            Name = name,
            Version = version,
            InstallDirectory = install,
            EntryPaths = entries,
            Dependencies = dependencies,
            CompatibleGameVersions = compatible,
            Repository = repository,
            Description = string.IsNullOrWhiteSpace(manifest.Description) ? null : Limit(manifest.Description.Trim(), 500)
        };
    }

    public static string NormalizeArchiveEntry(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (value.Contains('\\'))
            throw new PluginManagementException(422, "PLUGIN_ARCHIVE_PATH_INVALID", "ZIP 条目必须使用正斜杠路径。", value);
        var normalized = value.Trim('/');
        if (Path.IsPathRooted(normalized)
            || normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Contains(':')
            || normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(static segment => segment is "." or ".."))
            throw new PluginManagementException(422, "PLUGIN_ARCHIVE_PATH_INVALID", "ZIP 包含不安全路径。", value);
        return string.Join('/', normalized.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }

    private async Task<PackageExtraction> ExtractPackageAsync(string archivePath, string contentRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(contentRoot);
        using var zip = ZipFile.OpenRead(archivePath);
        if (zip.Entries.Count > MaximumEntries)
            throw new PluginManagementException(422, "PLUGIN_ARCHIVE_ENTRY_LIMIT", $"ZIP 条目超过 {MaximumEntries} 个限制。");
        var manifestEntries = zip.Entries
            .Where(entry => entry.FullName.Equals("palops-package.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (manifestEntries.Length == 0)
            throw new PluginManagementException(422, "PLUGIN_MANIFEST_MISSING", "ZIP 根目录缺少 palops-package.json。");
        if (manifestEntries.Length > 1)
            throw new PluginManagementException(422, "PLUGIN_MANIFEST_DUPLICATE", "ZIP 根目录包含重复的 palops-package.json。");
        var manifestEntry = manifestEntries[0];
        if (IsSymbolicLink(manifestEntry))
            throw new PluginManagementException(422, "PLUGIN_ARCHIVE_SYMLINK", "palops-package.json 不能是符号链接条目。", manifestEntry.FullName);
        if (manifestEntry.Length <= 0 || manifestEntry.Length > MaximumManifestBytes)
            throw new PluginManagementException(422, "PLUGIN_MANIFEST_TOO_LARGE", "palops-package.json 必须小于 256 KiB。");
        PluginPackageManifest manifest;
        await using (var stream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<PluginPackageManifest>(stream, JsonOptions, cancellationToken)
                ?? throw new PluginManagementException(422, "PLUGIN_MANIFEST_INVALID", "palops-package.json 内容为空。");
        }
        manifest = ValidateManifest(manifest);

        long total = 0;
        var files = new List<string>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry == manifestEntry) continue;
            if (IsSymbolicLink(entry))
                throw new PluginManagementException(422, "PLUGIN_ARCHIVE_SYMLINK", "ZIP 包含符号链接条目。", entry.FullName);
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var relative = NormalizeArchiveEntry(entry.FullName);
            EnsureNotReservedManagedPath(relative, "ZIP 条目");
            if (!targets.Add(relative))
                throw new PluginManagementException(422, "PLUGIN_ARCHIVE_DUPLICATE_PATH", "ZIP 包含重复目标路径。", relative);
            if (entry.Length < 0 || entry.Length > MaximumArchiveBytes)
                throw new PluginManagementException(422, "PLUGIN_ARCHIVE_ENTRY_TOO_LARGE", "ZIP 单个文件超过 256 MiB 限制。", relative);
            total = checked(total + entry.Length);
            if (total > MaximumExtractedBytes)
                throw new PluginManagementException(422, "PLUGIN_ARCHIVE_EXPANDED_TOO_LARGE", "ZIP 解压后超过 1 GiB 限制。");
            if (entry.Length > 10 * 1024 * 1024 && entry.CompressedLength > 0 && entry.Length / Math.Max(1, entry.CompressedLength) > 1000)
                throw new PluginManagementException(422, "PLUGIN_ARCHIVE_COMPRESSION_RATIO", "ZIP 文件压缩比异常，已拒绝处理。", relative);
            var target = Path.GetFullPath(Path.Combine(contentRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            EnsureContained(contentRoot, target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
            await input.CopyToAsync(output, 128 * 1024, cancellationToken);
            await output.FlushAsync(cancellationToken);
            output.Flush(true);
            files.Add(relative);
        }
        foreach (var entryPath in manifest.EntryPaths)
            if (!File.Exists(Path.Combine(contentRoot, entryPath.Replace('/', Path.DirectorySeparatorChar))))
                throw new PluginManagementException(422, "PLUGIN_ENTRY_PATH_MISSING", "ZIP 缺少清单声明的入口文件。", entryPath);
        return new(manifest, contentRoot, files.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private async Task<PluginBackupRecord> CreateBackupAsync(
        PluginManagementPaths paths,
        PluginPackageManifest manifest,
        ManagedPluginRegistration? previous,
        IReadOnlyList<string> affected,
        string userName,
        CancellationToken cancellationToken)
    {
        var backupId = "backup-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var fileName = backupId + ".zip";
        var archivePath = Path.Combine(paths.BackupRoot, fileName);
        var temporary = archivePath + ".tmp";
        var stored = new List<string>();
        try
        {
            await using (var fileStream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 128 * 1024, true))
            {
                using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var relative in affected.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var canonical = pathResolver.ResolveServerRelativePath(paths, relative);
                        foreach (var existing in new[] { canonical, canonical + ".palops-disabled" }.Where(File.Exists))
                        {
                            var exactRelative = pathResolver.ToServerRelativePath(paths, existing);
                            var entry = zip.CreateEntry("files/" + exactRelative.Replace('\\', '/'), CompressionLevel.Optimal);
                            await using var input = new FileStream(existing, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 128 * 1024, true);
                            await using var output = entry.Open();
                            await input.CopyToAsync(output, 128 * 1024, cancellationToken);
                            stored.Add(exactRelative);
                        }
                    }
                    var metadata = new BackupArchiveManifest(1, backupId, manifest.Id, previous, stored);
                    var metadataEntry = zip.CreateEntry("palops-plugin-backup.json", CompressionLevel.Optimal);
                    await using var metadataStream = metadataEntry.Open();
                    await JsonSerializer.SerializeAsync(metadataStream, metadata, JsonOptions, cancellationToken);
                }
                await fileStream.FlushAsync(cancellationToken);
                fileStream.Flush(true);
            }
            File.Move(temporary, archivePath, true);
            var info = new FileInfo(archivePath);
            return new(
                backupId,
                manifest.Id,
                manifest.Name,
                previous is null ? "install" : "update",
                fileName,
                await HashFileAsync(archivePath, cancellationToken),
                info.Length,
                DateTimeOffset.UtcNow,
                NormalizeUser(userName),
                false,
                null,
                null,
                previous,
                affected);
        }
        finally { TryDeleteFile(temporary); }
    }

    private async Task DeployAsync(
        PluginManagementPaths paths,
        string contentRoot,
        string installRoot,
        IReadOnlyList<string> affectedFiles,
        IReadOnlyList<string> installedFiles,
        bool enabled,
        string operationId,
        CancellationToken cancellationToken)
    {
        var prepared = new List<(string Temporary, string Destination)>();
        var originals = new List<(string Temporary, string Original)>();
        var installedTargets = new List<string>();
        var completed = false;
        try
        {
            foreach (var source in Directory.EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(contentRoot, source);
                var canonical = Path.GetFullPath(Path.Combine(installRoot, relative));
                EnsureContained(installRoot, canonical);
                var destination = enabled ? canonical : canonical + ".palops-disabled";
                pathResolver.EnsureSafeExistingPath(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var temporary = destination + ".palops-new-" + operationId;
                await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
                {
                    await input.CopyToAsync(output, 128 * 1024, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                    output.Flush(true);
                }
                prepared.Add((temporary, destination));
            }

            var mutationPaths = affectedFiles
                .SelectMany(relative =>
                {
                    var canonical = pathResolver.ResolveServerRelativePath(paths, relative);
                    return new[] { canonical, canonical + ".palops-disabled" };
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var existing in mutationPaths.Where(File.Exists))
            {
                cancellationToken.ThrowIfCancellationRequested();
                pathResolver.EnsureSafeExistingPath(existing);
                var temporary = existing + ".palops-old-" + operationId;
                pathResolver.EnsureSafeExistingPath(temporary);
                if (File.Exists(temporary))
                    throw new PluginManagementException(409, "PLUGIN_DEPLOY_TEMP_CONFLICT", "插件部署临时文件已存在，已拒绝覆盖。", temporary);
                File.Move(existing, temporary, false);
                originals.Add((temporary, existing));
            }

            foreach (var item in prepared)
            {
                File.Move(item.Temporary, item.Destination, false);
                installedTargets.Add(item.Destination);
            }
            completed = true;
        }
        catch (Exception deployError)
        {
            var recoveryErrors = RollbackMovedFiles(installedTargets, originals);
            if (recoveryErrors.Count > 0)
            {
                recoveryErrors.Insert(0, deployError);
                throw new PluginManagementException(
                    500,
                    "PLUGIN_DEPLOY_TRANSACTION_FAILED",
                    "插件部署失败，且原文件恢复不完整。请保留 .palops-old-* 文件并人工检查。",
                    deployError.Message,
                    "检查插件目录中的 .palops-old-* 和 .palops-new-* 文件。",
                    new AggregateException(recoveryErrors));
            }
            throw;
        }
        finally
        {
            foreach (var item in prepared) TryDeleteFile(item.Temporary);
            if (completed) foreach (var item in originals) TryDeleteFile(item.Temporary);
        }

        foreach (var relative in affectedFiles.Except(installedFiles, StringComparer.OrdinalIgnoreCase))
            RemoveEmptyParents(pathResolver.ResolveServerRelativePath(paths, relative), paths.ServerRoot);
    }

    private async Task RestoreBackupFilesAsync(
        PluginManagementPaths paths,
        PluginBackupRecord backup,
        IReadOnlyList<string> affected,
        CancellationToken cancellationToken)
    {
        var archivePath = ResolveBackupArchivePath(paths, backup.ArchiveFileName);
        if (!File.Exists(archivePath)) throw new FileNotFoundException("插件备份文件不存在。", archivePath);
        pathResolver.EnsureSafeExistingPath(archivePath);
        var actualHash = await HashFileAsync(archivePath, cancellationToken);
        if (!actualHash.Equals(backup.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            throw new PluginManagementException(409, "PLUGIN_BACKUP_HASH_MISMATCH", "插件备份 SHA-256 不匹配，已拒绝回滚。", archivePath);

        using var zip = ZipFile.OpenRead(archivePath);
        var metadataEntry = zip.GetEntry("palops-plugin-backup.json")
            ?? throw new InvalidDataException("插件备份缺少 palops-plugin-backup.json。");
        BackupArchiveManifest metadata;
        await using (var stream = metadataEntry.Open())
        {
            metadata = await JsonSerializer.DeserializeAsync<BackupArchiveManifest>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("插件备份元数据无效。");
        }
        if (metadata.SchemaVersion != 1
            || !metadata.BackupId.Equals(backup.BackupId, StringComparison.OrdinalIgnoreCase)
            || !metadata.PackageId.Equals(backup.PackageId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("插件备份元数据与记录不匹配。");

        var transactionId = Guid.NewGuid().ToString("N");
        var prepared = new List<(string Temporary, string Target)>();
        var originals = new List<(string Temporary, string Original)>();
        var installedTargets = new List<string>();
        var targetSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var completed = false;
        try
        {
            foreach (var relative in metadata.StoredPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalized = PluginManagementPathResolver.NormalizeRelativePath(relative, "备份文件路径");
                var entry = zip.GetEntry("files/" + normalized)
                    ?? throw new InvalidDataException($"插件备份缺少文件：{normalized}");
                var target = pathResolver.ResolveServerRelativePath(paths, normalized);
                if (!targetSet.Add(target))
                    throw new InvalidDataException($"插件备份包含重复目标：{normalized}");
                pathResolver.EnsureSafeExistingPath(target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temporary = target + ".palops-restore-" + transactionId;
                await using (var input = entry.Open())
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
                {
                    await input.CopyToAsync(output, 128 * 1024, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                    output.Flush(true);
                }
                prepared.Add((temporary, target));
            }

            var mutationPaths = affected
                .SelectMany(relative =>
                {
                    var canonical = pathResolver.ResolveServerRelativePath(paths, relative);
                    return new[] { canonical, canonical + ".palops-disabled" };
                })
                .Concat(prepared.Select(static item => item.Target))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var existing in mutationPaths.Where(File.Exists))
            {
                pathResolver.EnsureSafeExistingPath(existing);
                var temporary = existing + ".palops-old-" + transactionId;
                File.Move(existing, temporary, false);
                originals.Add((temporary, existing));
            }

            foreach (var item in prepared)
            {
                File.Move(item.Temporary, item.Target, false);
                installedTargets.Add(item.Target);
            }
            completed = true;
        }
        catch (Exception restoreError)
        {
            var recoveryErrors = new List<Exception>();
            for (var index = installedTargets.Count - 1; index >= 0; index--)
            {
                try { TryDeleteFile(installedTargets[index]); }
                catch (Exception ex) { recoveryErrors.Add(ex); }
            }
            for (var index = originals.Count - 1; index >= 0; index--)
            {
                try
                {
                    if (File.Exists(originals[index].Temporary))
                        File.Move(originals[index].Temporary, originals[index].Original, true);
                }
                catch (Exception ex) { recoveryErrors.Add(ex); }
            }
            if (recoveryErrors.Count > 0)
            {
                recoveryErrors.Insert(0, restoreError);
                throw new PluginManagementException(
                    500,
                    "PLUGIN_ROLLBACK_TRANSACTION_FAILED",
                    "插件回滚失败，且原文件恢复不完整。请保留 .palops-old-* 文件并人工检查。",
                    restoreError.Message,
                    $"备份文件：{backup.ArchiveFileName}",
                    new AggregateException(recoveryErrors));
            }
            throw;
        }
        finally
        {
            foreach (var item in prepared) TryDeleteFile(item.Temporary);
            if (completed) foreach (var item in originals) TryDeleteFile(item.Temporary);
        }
    }

    private static List<Exception> RollbackMovedFiles(
        IReadOnlyList<string> installedTargets,
        IReadOnlyList<(string Temporary, string Original)> originals)
    {
        var recoveryErrors = new List<Exception>();
        for (var index = installedTargets.Count - 1; index >= 0; index--)
        {
            try
            {
                if (File.Exists(installedTargets[index])) File.Delete(installedTargets[index]);
            }
            catch (Exception ex) { recoveryErrors.Add(ex); }
        }
        for (var index = originals.Count - 1; index >= 0; index--)
        {
            try
            {
                if (File.Exists(originals[index].Temporary))
                    File.Move(originals[index].Temporary, originals[index].Original, true);
            }
            catch (Exception ex) { recoveryErrors.Add(ex); }
        }
        return recoveryErrors;
    }

    private static void ValidateCompatibility(PluginPackageManifest manifest, string currentGameVersion, bool acknowledged)
    {
        if (manifest.CompatibleGameVersions.Count == 0 || string.IsNullOrWhiteSpace(currentGameVersion)) return;
        if (!PluginInventoryScanner.IsGameVersionCompatible(currentGameVersion, manifest.CompatibleGameVersions) && !acknowledged)
            throw new PluginManagementException(
                409,
                "PLUGIN_GAME_VERSION_INCOMPATIBLE",
                "插件包声明的游戏版本与当前 PalServer 不兼容。",
                $"当前：{currentGameVersion}；声明：{string.Join(", ", manifest.CompatibleGameVersions)}",
                "确认来源和兼容性后，勾选兼容风险确认再安装。");
    }

    private static void ValidateDependencies(PluginPackageManifest manifest, IReadOnlyList<PluginInventoryItem> inventory)
    {
        var byId = inventory.ToDictionary(static item => item.Id, StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        foreach (var dependency in manifest.Dependencies.Where(static item => !item.Optional))
        {
            if (!byId.TryGetValue(dependency.PackageId, out var package))
            {
                failures.Add($"缺少 {dependency.PackageId}");
                continue;
            }
            if (!package.Enabled) failures.Add($"{dependency.PackageId} 未启用");
            else if (!string.IsNullOrWhiteSpace(dependency.MinimumVersion)
                     && !PluginInventoryScanner.VersionAtLeast(package.Version, dependency.MinimumVersion))
                failures.Add($"{dependency.PackageId} 需要 >= {dependency.MinimumVersion}，当前 {package.Version}");
        }
        if (failures.Count > 0)
            throw new PluginManagementException(409, "PLUGIN_DEPENDENCY_UNSATISFIED", "插件依赖未满足。", string.Join("；", failures));
    }

    public static void ValidateDependencyGraph(
        PluginPackageManifest candidate,
        IReadOnlyDictionary<string, ManagedPluginRegistration> installedPackages)
    {
        var graph = installedPackages.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Dependencies
                .Where(static dependency => !dependency.Optional)
                .Select(static dependency => dependency.PackageId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);
        graph[candidate.Id] = candidate.Dependencies
            .Where(static dependency => !dependency.Optional)
            .Select(static dependency => dependency.PackageId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new Stack<string>();

        void Visit(string packageId)
        {
            if (visited.Contains(packageId) || !graph.TryGetValue(packageId, out var dependencies)) return;
            if (!visiting.Add(packageId))
            {
                var cycle = path.Reverse().Append(packageId).ToArray();
                throw new PluginManagementException(422, "PLUGIN_DEPENDENCY_CYCLE", "插件依赖关系形成循环。", string.Join(" -> ", cycle));
            }
            path.Push(packageId);
            foreach (var dependency in dependencies) Visit(dependency);
            path.Pop();
            visiting.Remove(packageId);
            visited.Add(packageId);
        }

        foreach (var packageId in graph.Keys.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)) Visit(packageId);
    }

    public static void ValidateEnabledDependentsForVersion(
        string packageId,
        string? candidateVersion,
        IReadOnlyDictionary<string, ManagedPluginRegistration> installedPackages)
    {
        var failures = new List<string>();
        foreach (var dependent in installedPackages.Values
                     .Where(item => item.Enabled && !item.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var dependency in dependent.Dependencies.Where(item =>
                         !item.Optional && item.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrWhiteSpace(candidateVersion))
                {
                    failures.Add($"{dependent.Name} 仍依赖该组件");
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(dependency.MinimumVersion)
                    && !PluginInventoryScanner.VersionAtLeast(candidateVersion, dependency.MinimumVersion))
                    failures.Add($"{dependent.Name} 需要 {packageId} >= {dependency.MinimumVersion}，目标版本为 {candidateVersion}");
            }
        }
        if (failures.Count > 0)
            throw new PluginManagementException(
                409,
                "PLUGIN_DEPENDENT_VERSION_CONFLICT",
                "该操作会破坏已启用插件的依赖关系。",
                string.Join("；", failures));
    }

    private static void ValidateNoEnabledDependents(string packageId, PluginManagementState state)
        => ValidateEnabledDependentsForVersion(packageId, null, state.Packages);

    private void EnsureOverwriteAllowed(
        PluginManagementPaths paths,
        ManagedPluginRegistration? previous,
        IReadOnlyList<string> installedFiles,
        bool overwriteExisting)
    {
        if (previous is not null || overwriteExisting) return;
        var conflicts = installedFiles.Where(relative =>
        {
            var path = pathResolver.ResolveServerRelativePath(paths, relative);
            return File.Exists(path) || File.Exists(path + ".palops-disabled");
        }).Take(20).ToArray();
        if (conflicts.Length > 0)
            throw new PluginManagementException(
                409,
                "PLUGIN_EXISTING_FILES_REQUIRE_ACKNOWLEDGEMENT",
                "安装包会覆盖现有文件，必须显式确认。",
                string.Join("；", conflicts));
    }

    private async Task EnsureServerStoppedAsync(CancellationToken cancellationToken)
    {
        var status = await runtimeCoordinator.RefreshAsync(false, cancellationToken);
        if (status.Process.ProcessId.HasValue
            && !status.State.Equals(PalServerRuntimeState.Stopped.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new PluginManagementException(409, "PLUGIN_SERVER_MUST_BE_STOPPED", "修改插件前必须先停止 PalServer。", $"当前状态：{status.State}");
    }

    private static string ResolveKnownRepository(string packageId)
        => packageId.ToLowerInvariant() switch
        {
            "paldefender" => "Ultimeit/PalDefender",
            "ue4ss" => "UE4SS-RE/RE-UE4SS",
            _ => string.Empty
        };

    private static string ResolveRepository(ManagedPluginRegistration package)
        => string.IsNullOrWhiteSpace(package.Repository) ? package.Id.ToLowerInvariant() switch
        {
            "paldefender" => "Ultimeit/PalDefender",
            "ue4ss" => "UE4SS-RE/RE-UE4SS",
            _ => string.Empty
        } : PluginReleaseClient.NormalizeRepository(package.Repository);

    private static string ResolveRepository(PluginPackageManifest manifest)
        => string.IsNullOrWhiteSpace(manifest.Repository) ? manifest.Id.ToLowerInvariant() switch
        {
            "paldefender" => "Ultimeit/PalDefender",
            "ue4ss" => "UE4SS-RE/RE-UE4SS",
            _ => string.Empty
        } : PluginReleaseClient.NormalizeRepository(manifest.Repository);

    private async Task<PluginManagementDashboard> ScanAfterCommitAsync(
        PluginManagementDashboard fallback,
        CancellationToken cancellationToken)
    {
        try
        {
            return await scanner.ScanAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to refresh plugin inventory after a committed operation.");
            return fallback with
            {
                ScannedAt = DateTimeOffset.UtcNow,
                Warnings = fallback.Warnings
                    .Concat(["操作已完成，但刷新插件库存失败；请稍后手动刷新页面。"])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(100)
                    .ToArray()
            };
        }
    }

    private async Task TryWriteAuditAsync(
        string eventType,
        string outcome,
        string remoteIp,
        string summary,
        object? data,
        CancellationToken cancellationToken)
    {
        try
        {
            await audit.WriteAsync(eventType, outcome, remoteIp, summary, data, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to write audit event {EventType}; the plugin operation remains committed.", eventType);
        }
    }

    private async Task MarkBackupRestoredAsync(string backupId, string restoredBy, CancellationToken cancellationToken)
    {
        try
        {
            var state = await repository.GetAsync(cancellationToken);
            var index = state.Backups.FindIndex(item => item.BackupId.Equals(backupId, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;
            var current = state.Backups[index];
            state.Backups[index] = current with
            {
                Restored = true,
                RestoredAt = DateTimeOffset.UtcNow,
                RestoredBy = NormalizeUser(restoredBy)
            };
            await repository.SaveAsync(state, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to mark plugin backup {BackupId} as restored.", backupId);
        }
    }

    private async Task RecordFailureAsync(
        string operationId,
        string operation,
        string packageId,
        string packageName,
        string userName,
        string remoteIp,
        DateTimeOffset startedAt,
        Exception error,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await repository.GetAsync(cancellationToken);
            state.History.Insert(0, new(operationId, operation, packageId, packageName, "failed", NormalizeUser(userName), NormalizeIp(remoteIp), startedAt, DateTimeOffset.UtcNow, $"插件{operation}操作失败。", Limit(error.Message, 500)));
            await repository.SaveAsync(state, cancellationToken);
            await audit.WriteAsync("plugin." + operation, "failed", remoteIp, $"插件{operation}操作失败。", new { operationId, packageId, error = Limit(error.Message, 500) }, cancellationToken);
        }
        catch (Exception recordError) { logger.LogError(recordError, "Unable to record plugin operation failure {OperationId}.", operationId); }
    }

    private static async Task CopyBoundedAsync(Stream source, string target, long maximum, CancellationToken cancellationToken)
    {
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
        var buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            total = checked(total + read);
            if (total > maximum) throw new PluginManagementException(413, "PLUGIN_ARCHIVE_TOO_LARGE", "插件 ZIP 超过 256 MiB 限制。");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await output.FlushAsync(cancellationToken);
        output.Flush(true);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
        var dosAttributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
        return unixMode == 0xA000 || (dosAttributes & FileAttributes.ReparsePoint) != 0;
    }

    private static void EnsureContained(string root, string target)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(target));
        if (relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
            throw new PluginManagementException(422, "PLUGIN_PATH_ESCAPE", "插件文件路径越过目标目录。", target);
    }

    private static string ResolveBackupArchivePath(PluginManagementPaths paths, string archiveFileName)
    {
        if (string.IsNullOrWhiteSpace(archiveFileName)
            || !archiveFileName.Equals(Path.GetFileName(archiveFileName), StringComparison.Ordinal)
            || !Path.GetExtension(archiveFileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new PluginManagementException(422, "PLUGIN_BACKUP_PATH_INVALID", "插件备份文件名无效。", archiveFileName);
        var full = Path.GetFullPath(Path.Combine(paths.BackupRoot, archiveFileName));
        EnsureContained(paths.BackupRoot, full);
        return full;
    }

    private static IReadOnlyList<PluginBackupRecord> ApplyBackupRetention(PluginManagementState state)
    {
        var ordered = state.Backups.OrderByDescending(static item => item.CreatedAt).ToArray();
        state.Backups = ordered.Take(100).ToList();
        return ordered.Skip(100).ToArray();
    }

    private static void DeleteExpiredBackups(PluginManagementPaths paths, IReadOnlyList<PluginBackupRecord> expired)
    {
        foreach (var item in expired)
        {
            try { TryDeleteFile(ResolveBackupArchivePath(paths, item.ArchiveFileName)); }
            catch { }
        }
    }

    private static void RemoveEmptyParents(string filePath, string stopRoot)
    {
        var directory = Path.GetDirectoryName(filePath);
        while (!string.IsNullOrWhiteSpace(directory)
               && !Path.GetFullPath(directory).Equals(Path.GetFullPath(stopRoot), StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                else break;
            }
            catch { break; }
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static void EnsureNotReservedManagedPath(string relativePath, string label)
    {
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment =>
                segment.EndsWith(".palops-disabled", StringComparison.OrdinalIgnoreCase)
                || segment.Contains(".palops-new-", StringComparison.OrdinalIgnoreCase)
                || segment.Contains(".palops-old-", StringComparison.OrdinalIgnoreCase)
                || segment.Contains(".palops-restore-", StringComparison.OrdinalIgnoreCase)))
            throw new PluginManagementException(422, "PLUGIN_RESERVED_PATH", $"{label}使用了 PalOps 保留文件名。", relativePath);
    }

    private static string NormalizeId(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!IdPattern().IsMatch(normalized))
            throw new PluginManagementException(422, "PLUGIN_ID_INVALID", "插件 ID 仅允许小写字母、数字、点、下划线和连字符，长度 2-80。", value);
        return normalized;
    }

    private static string NormalizeText(string value, string label, int maximum)
    {
        var normalized = value?.Replace('\r', ' ').Replace('\n', ' ').Trim() ?? string.Empty;
        if (normalized.Length is < 1 || normalized.Length > maximum)
            throw new PluginManagementException(422, "PLUGIN_MANIFEST_INVALID", $"{label}长度必须为 1-{maximum}。", value);
        return normalized;
    }

    private static string NormalizeUser(string value) => Limit(string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim(), 100);
    private static string NormalizeIp(string value) => Limit(string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim(), 80);
    private static string Limit(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];
    private static void TryDeleteFile(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,78}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    private sealed record PackageExtraction(PluginPackageManifest Manifest, string ContentRoot, IReadOnlyList<string> Files);
    private sealed record BackupArchiveManifest(
        int SchemaVersion,
        string BackupId,
        string PackageId,
        ManagedPluginRegistration? PreviousRegistration,
        IReadOnlyList<string> StoredPaths);
}
