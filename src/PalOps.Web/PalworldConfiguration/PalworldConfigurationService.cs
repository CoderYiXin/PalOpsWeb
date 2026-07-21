using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PalOps.Web.Audit;
using PalOps.Web.Configuration;
using PalOps.Web.ServerRuntime;

namespace PalOps.Web.PalworldConfiguration;

public interface IPalworldConfigurationService
{
    Task<PalworldConfigurationSnapshot> GetAsync(CancellationToken cancellationToken = default);
    Task<PalworldConfigurationSnapshot> SetPathAsync(string path, CancellationToken cancellationToken = default);
    Task<PalworldConfigurationPreview> PreviewAsync(PalworldConfigurationPreviewRequest request, CancellationToken cancellationToken = default);
    Task<PalworldConfigurationSaveResult> SaveAsync(PalworldConfigurationSaveRequest request, bool restart, string userName, string remoteIp, CancellationToken cancellationToken = default);
}

public sealed class PalworldConfigurationService : IPalworldConfigurationService
{
    private const int MaximumFileBytes = 2 * 1024 * 1024;
    private readonly IPalworldConfigurationPathResolver _pathResolver;
    private readonly IPalServerRuntimeConfigurationStore _runtimeStore;
    private readonly IPalServerRuntimeCoordinator _runtimeCoordinator;
    private readonly IAuditLogService _audit;
    private readonly PalworldSettingsIniCodec _codec;
    private readonly PalworldConfigurationValidator _validator;
    private readonly PalworldConfigurationMetadata _metadata;
    private readonly string _backupDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PalworldConfigurationService(
        IPalworldConfigurationPathResolver pathResolver,
        IPalServerRuntimeConfigurationStore runtimeStore,
        IPalServerRuntimeCoordinator runtimeCoordinator,
        IAuditLogService audit,
        PalworldSettingsIniCodec codec,
        PalworldConfigurationValidator validator,
        PalworldConfigurationMetadata metadata,
        IHostEnvironment environment,
        IOptions<AppRuntimeOptions> options)
    {
        _pathResolver = pathResolver;
        _runtimeStore = runtimeStore;
        _runtimeCoordinator = runtimeCoordinator;
        _audit = audit;
        _codec = codec;
        _validator = validator;
        _metadata = metadata;
        var data = Path.IsPathRooted(options.Value.DataDirectory)
            ? options.Value.DataDirectory
            : Path.Combine(environment.ContentRootPath, options.Value.DataDirectory);
        _backupDirectory = Path.Combine(data, "config-backups", "palworld");
        Directory.CreateDirectory(_backupDirectory);
    }

    public async Task<PalworldConfigurationSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var paths = await _pathResolver.ResolveAsync(cancellationToken);
        return await LoadAsync(paths, cancellationToken);
    }

    public async Task<PalworldConfigurationSnapshot> SetPathAsync(string path, CancellationToken cancellationToken = default)
    {
        var paths = await _pathResolver.SetExplicitPathAsync(path, cancellationToken);
        return await LoadAsync(paths, cancellationToken);
    }

    public async Task<PalworldConfigurationPreview> PreviewAsync(PalworldConfigurationPreviewRequest request, CancellationToken cancellationToken = default)
    {
        var current = await GetAsync(cancellationToken);
        PalworldConfigurationValidationResult validation;
        try
        {
            var document = _codec.Parse(request.RawContent);
            validation = _validator.Validate(document, request.LaunchArguments ?? string.Empty, current.WorldOptionExists);
        }
        catch (InvalidDataException ex)
        {
            var diagnostic = new PalworldConfigurationDiagnostic("INI_SYNTAX_INVALID", PalworldConfigurationSeverity.Error, "rawContent", ex.Message);
            return new(false, request.RawContent ?? string.Empty, new Dictionary<string, string>(), [diagnostic], [], false, false);
        }
        var changes = BuildChanges(current.Settings, validation.Settings, current.LaunchArguments, request.LaunchArguments ?? string.Empty);
        return new(
            validation.Valid,
            validation.NormalizedContent,
            validation.Settings,
            validation.Diagnostics,
            changes,
            changes.Any(static item => item.RequiresRestart),
            validation.Diagnostics.Any(static item => item.Severity == PalworldConfigurationSeverity.Warning));
    }

    public async Task<PalworldConfigurationSaveResult> SaveAsync(
        PalworldConfigurationSaveRequest request,
        bool restart,
        string userName,
        string remoteIp,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var paths = await _pathResolver.ResolveAsync(cancellationToken);
            var current = await LoadAsync(paths, cancellationToken);
            EnsureConcurrency(current, request);
            var preview = await PreviewAgainstAsync(current, request, cancellationToken);
            if (!preview.Valid)
                throw new PalworldConfigurationException(422, "PALWORLD_CONFIGURATION_INVALID", "配置校验失败，请修正错误后再保存。", string.Join(Environment.NewLine, preview.Diagnostics.Where(static item => item.Severity == PalworldConfigurationSeverity.Error).Select(static item => item.Message)));
            if (preview.HasWarnings && !request.ConfirmWarnings)
                throw new PalworldConfigurationException(409, "PALWORLD_CONFIGURATION_WARNING_CONFIRMATION_REQUIRED", "配置包含风险警告，需要确认后才能保存。", string.Join(Environment.NewLine, preview.Diagnostics.Where(static item => item.Severity == PalworldConfigurationSeverity.Warning).Select(static item => item.Message)), "在差异确认窗口中勾选确认风险后重新保存。");

            var runtime = await _runtimeStore.GetAsync(cancellationToken);
            var launchChanged = !string.Equals(runtime.Arguments?.Trim(), request.LaunchArguments?.Trim(), StringComparison.Ordinal);
            if (launchChanged && !runtime.Confirmed)
                throw new PalworldConfigurationException(409, "PALSERVER_CONFIGURATION_NOT_CONFIRMED", "启动参数发生变化，但 PalServer 启动配置尚未确认。", null, "请先在服务器运行控制中确认 PalServer 启动方式和路径。");

            Directory.CreateDirectory(Path.GetDirectoryName(paths.ConfigurationPath)!);
            var backupPath = await BackupAsync(paths.ConfigurationPath, cancellationToken);
            string? writtenTemporary = null;
            var runtimeChanged = false;
            try
            {
                writtenTemporary = await WriteTemporaryAsync(paths.ConfigurationPath, preview.NormalizedContent, cancellationToken);
                if (launchChanged)
                {
                    await _runtimeStore.SaveAsync(runtime with
                    {
                        Arguments = request.LaunchArguments?.Trim() ?? string.Empty,
                        UpdatedAt = DateTimeOffset.UtcNow,
                        UpdatedBy = userName
                    }, cancellationToken);
                    runtimeChanged = true;
                }
                File.Move(writtenTemporary, paths.ConfigurationPath, true);
                writtenTemporary = null;
            }
            catch
            {
                if (runtimeChanged)
                {
                    try { await _runtimeStore.SaveAsync(runtime with { UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = "rollback" }, CancellationToken.None); }
                    catch { }
                }
                if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath))
                {
                    try { File.Copy(backupPath, paths.ConfigurationPath, true); } catch { }
                }
                throw;
            }
            finally
            {
                if (writtenTemporary is not null)
                {
                    try { File.Delete(writtenTemporary); } catch { }
                }
            }

            await TrimBackupsAsync(cancellationToken);
            await _audit.WriteAsync(
                "palworld.configuration.save",
                "success",
                remoteIp,
                restart ? "已保存 Palworld 配置并请求安全重启。" : "已保存 Palworld 配置。",
                new
                {
                    paths.ConfigurationPath,
                    BackupPath = backupPath,
                    ChangeCount = preview.Changes.Count,
                    LaunchArgumentsChanged = launchChanged,
                    RestartRequested = restart,
                    Settings = preview.Changes.Where(static item => !item.Sensitive).Select(static item => new { item.Source, item.Key }).ToArray()
                },
                cancellationToken);

            ServerOperationSnapshot? operation = null;
            if (restart)
                operation = await _runtimeCoordinator.RestartAsync(userName, remoteIp, cancellationToken);
            var saved = await LoadAsync(paths, cancellationToken);
            return new(saved, backupPath, restart, operation?.OperationId);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
            {
                try { await _audit.WriteAsync("palworld.configuration.save", "failed", remoteIp, ex.Message, null, CancellationToken.None); }
                catch { }
            }
            throw;
        }
        finally { _gate.Release(); }
    }

    private async Task<PalworldConfigurationPreview> PreviewAgainstAsync(PalworldConfigurationSnapshot current, PalworldConfigurationSaveRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var document = _codec.Parse(request.RawContent);
            var validation = _validator.Validate(document, request.LaunchArguments ?? string.Empty, current.WorldOptionExists);
            var changes = BuildChanges(current.Settings, validation.Settings, current.LaunchArguments, request.LaunchArguments ?? string.Empty);
            return new(validation.Valid, validation.NormalizedContent, validation.Settings, validation.Diagnostics, changes, changes.Any(static item => item.RequiresRestart), validation.Diagnostics.Any(static item => item.Severity == PalworldConfigurationSeverity.Warning));
        }
        catch (InvalidDataException ex)
        {
            return new(false, request.RawContent ?? string.Empty, new Dictionary<string, string>(), [new("INI_SYNTAX_INVALID", PalworldConfigurationSeverity.Error, "rawContent", ex.Message)], [], false, false);
        }
    }

    private async Task<PalworldConfigurationSnapshot> LoadAsync(PalworldConfigurationPaths paths, CancellationToken cancellationToken)
    {
        string raw;
        string sha;
        long size;
        DateTimeOffset? modified;
        var diagnostics = new List<PalworldConfigurationDiagnostic>();
        IReadOnlyDictionary<string, string> settings;
        if (File.Exists(paths.ConfigurationPath))
        {
            var info = new FileInfo(paths.ConfigurationPath);
            if (info.Length > MaximumFileBytes)
                throw new PalworldConfigurationException(422, "PALWORLD_CONFIGURATION_TOO_LARGE", "PalWorldSettings.ini 超过 2 MiB 限制。", paths.ConfigurationPath);
            var bytes = await File.ReadAllBytesAsync(paths.ConfigurationPath, cancellationToken);
            if (bytes.Length > MaximumFileBytes)
                throw new PalworldConfigurationException(422, "PALWORLD_CONFIGURATION_TOO_LARGE", "PalWorldSettings.ini 超过 2 MiB 限制。", paths.ConfigurationPath);
            raw = Encoding.UTF8.GetString(bytes);
            size = bytes.LongLength;
            modified = info.LastWriteTimeUtc;
            sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            try
            {
                var document = _codec.Parse(raw);
                var runtimeForValidation = await _runtimeStore.GetAsync(cancellationToken);
                var validation = _validator.Validate(document, runtimeForValidation.Arguments, File.Exists(paths.WorldOptionPath));
                settings = validation.Settings;
                diagnostics.AddRange(validation.Diagnostics);
            }
            catch (InvalidDataException ex)
            {
                settings = new Dictionary<string, string>();
                diagnostics.Add(new("INI_SYNTAX_INVALID", PalworldConfigurationSeverity.Error, "rawContent", ex.Message));
            }
        }
        else
        {
            raw = CreateStarterContent();
            size = 0;
            modified = null;
            sha = string.Empty;
            settings = _codec.Parse(raw).ToDictionary();
            diagnostics.Add(new("CONFIGURATION_FILE_NOT_FOUND", PalworldConfigurationSeverity.Warning, "configurationPath", "未找到 PalWorldSettings.ini；保存时将创建该文件。"));
        }

        var runtime = await _runtimeStore.GetAsync(cancellationToken);
        var worldOptionInfo = string.IsNullOrWhiteSpace(paths.WorldOptionPath) ? null : new FileInfo(paths.WorldOptionPath);
        return new(
            paths.ConfigurationPath,
            File.Exists(paths.ConfigurationPath),
            paths.Source,
            raw,
            sha,
            size,
            modified,
            settings,
            runtime.Arguments,
            runtime.Confirmed,
            runtime.UpdatedAt,
            paths.WorldOptionPath,
            worldOptionInfo?.Exists == true,
            worldOptionInfo?.Exists == true ? worldOptionInfo.Length : null,
            worldOptionInfo?.Exists == true ? worldOptionInfo.LastWriteTimeUtc : null,
            diagnostics,
            _metadata.ToResponse(settings));
    }

    private IReadOnlyList<PalworldConfigurationChange> BuildChanges(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after,
        string beforeArguments,
        string afterArguments)
    {
        var keys = before.Keys.Concat(after.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static key => key, StringComparer.OrdinalIgnoreCase);
        var changes = new List<PalworldConfigurationChange>();
        foreach (var key in keys)
        {
            before.TryGetValue(key, out var oldValue);
            after.TryGetValue(key, out var newValue);
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) continue;
            var field = _metadata.Find(key);
            changes.Add(new("ini", key, field?.Sensitive == true ? "••••••" : oldValue, field?.Sensitive == true ? "••••••" : newValue, field?.RequiresRestart ?? true, field?.Sensitive ?? false));
        }
        if (!string.Equals(beforeArguments?.Trim(), afterArguments?.Trim(), StringComparison.Ordinal))
            changes.Add(new("launch", "Arguments", beforeArguments?.Trim(), afterArguments?.Trim(), true, false));
        return changes;
    }

    private static void EnsureConcurrency(PalworldConfigurationSnapshot current, PalworldConfigurationSaveRequest request)
    {
        var expected = request.ExpectedSha256?.Trim() ?? string.Empty;
        if (!string.Equals(expected, current.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new PalworldConfigurationException(409, "PALWORLD_CONFIGURATION_CHANGED", "配置文件已被其他进程修改，请重新加载后再保存。", $"Expected SHA-256: {expected}; Current SHA-256: {current.Sha256}");
        if (request.ExpectedRuntimeConfigurationUpdatedAt.HasValue
            && current.RuntimeConfigurationConfirmed
            && current.RuntimeConfigurationUpdatedAt != request.ExpectedRuntimeConfigurationUpdatedAt.Value)
            throw new PalworldConfigurationException(409, "PALSERVER_RUNTIME_CONFIGURATION_CHANGED", "PalServer 启动配置已发生变化，请重新加载后再保存。");
    }

    private async Task<string> BackupAsync(string configurationPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(configurationPath)) return string.Empty;
        var sha = await Sha256Async(configurationPath, cancellationToken);
        var fileName = $"PalWorldSettings-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{sha[..12]}.ini";
        var backupPath = Path.Combine(_backupDirectory, fileName);
        File.Copy(configurationPath, backupPath, false);
        return backupPath;
    }

    private static async Task<string> WriteTemporaryAsync(string destination, string content, CancellationToken cancellationToken)
    {
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        await using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 32 * 1024, leaveOpen: true);
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(true);
        return temporary;
    }

    private async Task TrimBackupsAsync(CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(_backupDirectory, "PalWorldSettings-*.ini")
                     .Select(static path => new FileInfo(path))
                     .OrderByDescending(static file => file.CreationTimeUtc)
                     .Skip(50))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { file.Delete(); } catch { }
        }
        await Task.CompletedTask;
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string CreateStarterContent() =>
        "[/Script/Pal.PalGameWorldSettings]\n" +
        "OptionSettings=(ServerName=\"PalOps Palworld Server\",ServerDescription=\"\",AdminPassword=\"\",ServerPassword=\"\",PublicPort=8211,PublicIP=\"\",ServerPlayerMaxNum=32,RCONEnabled=False,RCONPort=25575,RESTAPIEnabled=False,RESTAPIPort=8212,bIsUseBackupSaveData=True,AutoSaveSpan=30,GuildPlayerMaxNum=20,BaseCampMaxNumInGuild=4,BaseCampWorkerMaxNum=15,ServerReplicatePawnCullDistance=15000.000000,DeathPenalty=All,bIsPvP=False,CrossplayPlatforms=(Steam,Xbox,PS5,Mac),DayTimeSpeedRate=1.000000,NightTimeSpeedRate=1.000000,ExpRate=1.000000,PalCaptureRate=1.000000,PalSpawnNumRate=1.000000,WorkSpeedRate=1.000000)\n";
}
