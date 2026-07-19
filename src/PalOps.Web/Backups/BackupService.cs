using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PalOps.Web.Contracts;
using PalOps.Web.Events;
using PalOps.Web.Infrastructure;
using PalOps.Web.Rcon;
using PalOps.Web.SaveGames;
using PalOps.Web.Settings;

namespace PalOps.Web.Backups;

public interface IBackupService
{
    Task<BackupSummaryV1> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BackupRecordV1>> ListAsync(CancellationToken cancellationToken = default);
    Task<BackupRecordV1> CreateAsync(string? note, bool? executeSaveFirst, CancellationToken cancellationToken = default);
    Task<BackupVerificationV1> VerifyAsync(string id, CancellationToken cancellationToken = default);
    Task<string> GetArchivePathAsync(string id, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<BackupRestorePreflightV1> GetRestorePreflightAsync(string id, CancellationToken cancellationToken = default);
    Task RestoreAsync(string id, string confirmation, string backupName, CancellationToken cancellationToken = default);
}

public sealed class BackupService(
    IBackupRepository repository,
    IServerSettingsStore settingsStore,
    IRuntimePathResolver paths,
    ISavePathGuard pathGuard,
    IRconClient rcon,
    IPalOpsEventPublisher eventPublisher,
    ILogger<BackupService> logger) : IBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<BackupSummaryV1> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        var directory = ResolveBackupDirectory(settings);
        var records = await repository.ListAsync(cancellationToken);
        return new BackupSummaryV1(
            directory,
            records.Count,
            records.Sum(x => Math.Max(0, x.SizeBytes)),
            records.OrderByDescending(x => x.CreatedAt).FirstOrDefault()?.CreatedAt,
            settings.Backup.RestoreEnabled,
            File.Exists(RestoreMarkerPath()));
    }

    public async Task<IReadOnlyList<BackupRecordV1>> ListAsync(CancellationToken cancellationToken = default)
        => (await repository.ListAsync(cancellationToken)).Select(ToContract).ToArray();

    public async Task<BackupRecordV1> CreateAsync(string? note, bool? executeSaveFirst, CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("已有备份任务正在执行。");
        try
        {
            var settings = await settingsStore.GetAsync(cancellationToken);
            var worldDirectory = pathGuard.NormalizeWorldDirectory(settings.SaveGame.WorldDirectory);
            if (string.IsNullOrWhiteSpace(worldDirectory) || !Directory.Exists(worldDirectory))
                throw new DirectoryNotFoundException("世界存档目录未配置或不存在。");
            var levelPath = Path.Combine(worldDirectory, "Level.sav");
            if (!File.Exists(levelPath)) throw new FileNotFoundException("世界存档目录中不存在 Level.sav。", levelPath);

            var saveFirst = executeSaveFirst ?? settings.Backup.ExecuteSaveFirst;
            if (saveFirst)
            {
                var result = await rcon.ExecuteAsync(settings.Rcon, "Save", cancellationToken);
                if (result.Response.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("服务器拒绝 Save 命令，已取消备份。");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }

            var backupDirectory = ResolveBackupDirectory(settings);
            EnsureBackupDirectoryOutsideWorld(worldDirectory, backupDirectory);
            var worldId = Path.GetFileName(worldDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var id = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
            var fileName = $"{SanitizeFileName(worldId)}-{id}.zip";
            var archivePath = Path.Combine(backupDirectory, fileName);
            var temporaryPath = archivePath + ".tmp";
            var record = new BackupRecord(id, fileName, DateTimeOffset.UtcNow, 0, string.Empty, "creating", NormalizeNote(note), worldId, 0, saveFirst, null, null);
            await repository.UpsertAsync(record, cancellationToken);

            try
            {
                var manifestFiles = new List<BackupManifestFile>();
                var compression = MapCompression(settings.Backup.CompressionLevel);
                await using (var fileStream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 128 * 1024, true))
                using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var sourcePath in EnumerateWorldFiles(worldDirectory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var relative = Path.GetRelativePath(worldDirectory, sourcePath).Replace('\\', '/');
                        var entry = archive.CreateEntry(relative, compression);
                        await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 128 * 1024, true))
                        await using (var output = entry.Open())
                            await input.CopyToAsync(output, 128 * 1024, cancellationToken);
                        var info = new FileInfo(sourcePath);
                        manifestFiles.Add(new BackupManifestFile(relative, info.Length, await HashFileAsync(sourcePath, cancellationToken)));
                    }

                    var manifest = new BackupManifest(id, worldId, record.CreatedAt, worldDirectory, manifestFiles);
                    var manifestEntry = archive.CreateEntry("palops-backup-manifest.json", CompressionLevel.Optimal);
                    await using var manifestStream = manifestEntry.Open();
                    await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
                }

                File.Move(temporaryPath, archivePath, true);
                var finalInfo = new FileInfo(archivePath);
                var completed = record with
                {
                    SizeBytes = finalInfo.Length,
                    Sha256 = await HashFileAsync(archivePath, cancellationToken),
                    Status = "ready",
                    FileCount = manifestFiles.Count,
                    Error = null
                };
                await repository.UpsertAsync(completed, cancellationToken);
                await ApplyRetentionAsync(settings.Backup.RetentionCount, backupDirectory, cancellationToken);
                await PublishBestEffortAsync(PalOpsEvent.Create(
                    "backup.completed",
                    backup: new Dictionary<string, object?>
                    {
                        ["fileName"] = completed.FileName,
                        ["size"] = completed.SizeBytes,
                        ["worldId"] = completed.WorldId,
                        ["note"] = completed.Note
                    },
                    metadata: new Dictionary<string, object?> { ["message"] = "存档备份已完成。" }));
                return ToContract(completed);
            }
            catch (Exception ex)
            {
                TryDelete(temporaryPath);
                var failed = record with { Status = "failed", Error = Limit(ex.Message, 500) };
                await repository.UpsertAsync(failed, CancellationToken.None);
                await PublishBestEffortAsync(PalOpsEvent.Create(
                    "backup.failed",
                    "error",
                    backup: new Dictionary<string, object?>
                    {
                        ["fileName"] = failed.FileName,
                        ["size"] = failed.SizeBytes,
                        ["worldId"] = failed.WorldId,
                        ["note"] = failed.Note
                    },
                    metadata: new Dictionary<string, object?>
                    {
                        ["message"] = failed.Error,
                        ["errorCode"] = ex.GetType().Name
                    }));
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<BackupVerificationV1> VerifyAsync(string id, CancellationToken cancellationToken = default)
    {
        var record = await RequireAsync(id, cancellationToken);
        var settings = await settingsStore.GetAsync(cancellationToken);
        var path = ResolveArchivePath(ResolveBackupDirectory(settings), record.FileName);
        if (!File.Exists(path))
        {
            var missing = record with { Status = "missing", VerifiedAt = DateTimeOffset.UtcNow, Error = "备份文件不存在。" };
            await repository.UpsertAsync(missing, cancellationToken);
            return new BackupVerificationV1(false, "missing", string.Empty, 0, missing.VerifiedAt.Value, missing.Error);
        }

        var valid = true;
        string? message = null;
        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.GetEntry("palops-backup-manifest.json") is null)
            {
                valid = false;
                message = "备份缺少 palops-backup-manifest.json。";
            }
            else
            {
                foreach (var entry in archive.Entries.Where(x => !string.IsNullOrEmpty(x.Name)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await using var stream = entry.Open();
                    var buffer = new byte[64 * 1024];
                    while (await stream.ReadAsync(buffer, cancellationToken) > 0) { }
                }
            }
        }
        catch (InvalidDataException ex)
        {
            valid = false;
            message = Limit(ex.Message, 500);
        }

        var sha256 = await HashFileAsync(path, cancellationToken);
        if (!string.IsNullOrWhiteSpace(record.Sha256) && !sha256.Equals(record.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            valid = false;
            message = "SHA-256 与备份索引不一致。";
        }

        var verifiedAt = DateTimeOffset.UtcNow;
        var updated = record with
        {
            SizeBytes = new FileInfo(path).Length,
            Sha256 = sha256,
            Status = valid ? "verified" : "invalid",
            VerifiedAt = verifiedAt,
            Error = message
        };
        await repository.UpsertAsync(updated, cancellationToken);
        await PublishBestEffortAsync(PalOpsEvent.Create(
            "backup.verified",
            valid ? "information" : "warning",
            backup: new Dictionary<string, object?>
            {
                ["fileName"] = updated.FileName,
                ["size"] = updated.SizeBytes,
                ["worldId"] = updated.WorldId,
                ["note"] = updated.Note
            },
            metadata: new Dictionary<string, object?>
            {
                ["message"] = valid ? "备份校验通过。" : message ?? "备份校验失败。",
                ["valid"] = valid
            }));
        return new BackupVerificationV1(valid, updated.Status, sha256, updated.SizeBytes, verifiedAt, message);
    }

    public async Task<string> GetArchivePathAsync(string id, CancellationToken cancellationToken = default)
    {
        var record = await RequireAsync(id, cancellationToken);
        var settings = await settingsStore.GetAsync(cancellationToken);
        var root = ResolveBackupDirectory(settings);
        var path = ResolveArchivePath(root, record.FileName);
        if (!File.Exists(path)) throw new FileNotFoundException("备份文件不存在。", path);
        return path;
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var record = await RequireAsync(id, cancellationToken);
        var settings = await settingsStore.GetAsync(cancellationToken);
        var path = ResolveArchivePath(ResolveBackupDirectory(settings), record.FileName);
        TryDelete(path);
        await repository.DeleteAsync(id, cancellationToken);
        await PublishBestEffortAsync(PalOpsEvent.Create(
            "backup.deleted",
            backup: new Dictionary<string, object?>
            {
                ["fileName"] = record.FileName,
                ["size"] = record.SizeBytes,
                ["worldId"] = record.WorldId,
                ["note"] = record.Note
            },
            metadata: new Dictionary<string, object?> { ["message"] = "备份记录已删除。" }));
    }

    public async Task<BackupRestorePreflightV1> GetRestorePreflightAsync(string id, CancellationToken cancellationToken = default)
    {
        var record = await RequireAsync(id, cancellationToken);
        var settings = await settingsStore.GetAsync(cancellationToken);
        var reasons = new List<string>();
        if (!settings.Backup.RestoreEnabled) reasons.Add("设置中尚未启用备份恢复。默认保持只读。 ");
        if (!File.Exists(RestoreMarkerPath())) reasons.Add("缺少服务器已停止确认标记文件。请停止服务器后创建该文件。");
        string archivePath;
        try
        {
            var backupDirectory = ResolveBackupDirectory(settings);
            var normalizedWorld = pathGuard.NormalizeWorldDirectory(settings.SaveGame.WorldDirectory);
            if (!string.IsNullOrWhiteSpace(normalizedWorld)) EnsureBackupDirectoryOutsideWorld(normalizedWorld, backupDirectory);
            archivePath = ResolveArchivePath(backupDirectory, record.FileName);
        }
        catch (InvalidOperationException ex)
        {
            archivePath = string.Empty;
            reasons.Add(ex.Message);
        }
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath)) reasons.Add("备份文件不存在。");
        if (record.Status is "invalid" or "failed" or "missing") reasons.Add("备份状态不允许恢复，请先通过校验。");
        var worldDirectory = pathGuard.NormalizeWorldDirectory(settings.SaveGame.WorldDirectory);
        if (string.IsNullOrWhiteSpace(worldDirectory)) reasons.Add("世界存档目录未配置。");
        return new BackupRestorePreflightV1(reasons.Count == 0, reasons, RestoreMarkerPath(), worldDirectory, archivePath);
    }

    public async Task RestoreAsync(string id, string confirmation, string backupName, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(confirmation?.Trim(), "CONFIRM", StringComparison.Ordinal))
            throw new ArgumentException("恢复备份必须输入 CONFIRM。");
        var record = await RequireAsync(id, cancellationToken);
        if (!string.Equals(backupName?.Trim(), record.FileName, StringComparison.Ordinal))
            throw new ArgumentException("输入的备份文件名不匹配。");
        var preflight = await GetRestorePreflightAsync(id, cancellationToken);
        if (!preflight.Allowed) throw new InvalidOperationException(string.Join("；", preflight.BlockingReasons));

        var verification = await VerifyAsync(id, cancellationToken);
        if (!verification.Valid) throw new InvalidOperationException("备份校验失败，禁止恢复。");

        // Create the rollback point before taking the restore gate; CreateAsync uses the same gate.
        await CreateAsync("恢复前自动备份", false, cancellationToken);

        if (!await _gate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("已有备份任务正在执行。");
        try
        {
            var settings = await settingsStore.GetAsync(cancellationToken);
            var worldDirectory = pathGuard.NormalizeWorldDirectory(settings.SaveGame.WorldDirectory);
            var parent = Directory.GetParent(worldDirectory)?.FullName ?? throw new InvalidOperationException("无法解析世界存档父目录。");
            var restoreRoot = Path.Combine(parent, ".palops-restore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(restoreRoot);
            try
            {
                ZipFile.ExtractToDirectory(preflight.BackupFile, restoreRoot, overwriteFiles: false);
                var manifestPath = Path.Combine(restoreRoot, "palops-backup-manifest.json");
                if (!File.Exists(manifestPath)) throw new InvalidDataException("恢复包缺少清单。");
                File.Delete(manifestPath);

                var oldPath = worldDirectory + ".palops-old-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
                Directory.Move(worldDirectory, oldPath);
                try
                {
                    Directory.Move(restoreRoot, worldDirectory);
                    Directory.Delete(oldPath, true);
                }
                catch
                {
                    if (!Directory.Exists(worldDirectory) && Directory.Exists(oldPath)) Directory.Move(oldPath, worldDirectory);
                    throw;
                }
                TryDelete(RestoreMarkerPath());
            }
            finally
            {
                if (Directory.Exists(restoreRoot)) Directory.Delete(restoreRoot, true);
            }
        }
        finally { _gate.Release(); }

        await PublishBestEffortAsync(PalOpsEvent.Create(
            "backup.restored",
            backup: new Dictionary<string, object?>
            {
                ["fileName"] = record.FileName,
                ["size"] = record.SizeBytes,
                ["worldId"] = record.WorldId,
                ["note"] = record.Note
            },
            metadata: new Dictionary<string, object?> { ["message"] = "备份恢复已完成。" }));
    }

    private async Task ApplyRetentionAsync(int retentionCount, string backupDirectory, CancellationToken cancellationToken)
    {
        var records = (await repository.ListAsync(cancellationToken)).OrderByDescending(x => x.CreatedAt).ToArray();
        foreach (var record in records.Skip(Math.Max(1, retentionCount)))
        {
            try { TryDelete(ResolveArchivePath(backupDirectory, record.FileName)); }
            catch (InvalidOperationException ex) { logger.LogWarning(ex, "Skipped unsafe backup record during retention. BackupId={BackupId}", record.Id); }
            await repository.DeleteAsync(record.Id, cancellationToken);
        }
    }

    private async Task PublishBestEffortAsync(PalOpsEvent palOpsEvent)
    {
        try { await eventPublisher.PublishAsync(palOpsEvent, CancellationToken.None); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Backup event {EventType} could not be published.", palOpsEvent.EventType);
        }
    }

    private async Task<BackupRecord> RequireAsync(string id, CancellationToken cancellationToken)
        => await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("备份记录不存在。");

    private string ResolveBackupDirectory(ServerSettings settings)
        => paths.ResolveConfiguredDirectory(settings.Backup.Directory, "backups/files");

    private static void EnsureBackupDirectoryOutsideWorld(string worldDirectory, string backupDirectory)
    {
        var worldRoot = Path.GetFullPath(worldDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var backupRoot = Path.GetFullPath(backupDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var worldPrefix = worldRoot + Path.DirectorySeparatorChar;
        if (backupRoot.Equals(worldRoot, PathComparison) || backupRoot.StartsWith(worldPrefix, PathComparison))
            throw new InvalidOperationException("备份目录不能位于世界存档目录内部，否则会递归包含正在创建的备份文件。");
    }

    private static string ResolveArchivePath(string root, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            throw new InvalidOperationException("备份索引中的文件名无效。");
        root = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(root, fileName));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, PathComparison))
            throw new InvalidOperationException("备份路径越过允许目录。");
        return path;
    }

    private string RestoreMarkerPath() => paths.ResolveDataPath("restore-server-stopped.flag");

    private static IEnumerable<string> EnumerateWorldFiles(string worldDirectory)
        => Directory.EnumerateFiles(worldDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + ".palops-", StringComparison.OrdinalIgnoreCase));

    private static CompressionLevel MapCompression(int value) => value switch
    {
        <= 0 => CompressionLevel.NoCompression,
        <= 3 => CompressionLevel.Fastest,
        >= 8 => CompressionLevel.SmallestSize,
        _ => CompressionLevel.Optimal
    };

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 128 * 1024, true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeNote(string? note)
    {
        var value = (note ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length <= 300 ? value : value[..300];
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "world" : safe;
    }

    private static string Limit(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static BackupRecordV1 ToContract(BackupRecord record) => new(
        record.Id,
        record.FileName,
        record.CreatedAt,
        record.SizeBytes,
        record.Sha256,
        record.Status,
        record.Note,
        record.WorldId,
        record.FileCount,
        record.ExecuteSaveFirst,
        record.VerifiedAt,
        record.Error);
}
