using Microsoft.Extensions.Options;
using PalOps.Web.Audit;
using PalOps.Web.Configuration;
using PalOps.Web.Contracts;
using PalOps.Web.External;
using PalOps.Web.Infrastructure;
using PalOps.Web.Rcon;
using PalOps.Web.SaveGames.Binary;
using PalOps.Web.Security;
using PalOps.Web.Settings;

namespace PalOps.Web.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/settings").RequireAuthorization("Administrator").WithTags("Settings");
        group.MapGet("/server", async (IServerSettingsStore store, CancellationToken cancellationToken)
            => Results.Ok(await store.GetSummaryAsync(cancellationToken)));
        group.MapPut("/server", SaveAsync).AddEndpointFilter<CsrfValidationFilter>();
        group.MapPost("/test", TestAsync).AddEndpointFilter<CsrfValidationFilter>();
        group.MapPost("/storage/initialize", InitializeStorageAsync)
            .AddEndpointFilter<CsrfValidationFilter>();
        return endpoints;
    }

    private static async Task<IResult> InitializeStorageAsync(
        HttpContext context,
        IStorageInitializationService storage,
        IAuditLogService audit,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var result = await storage.InitializeAsync(cancellationToken);
        await audit.WriteBestEffortAsync(
            loggerFactory.CreateLogger("PalOps.Audit"),
            "storage.initialize",
            "success",
            EndpointHelpers.RemoteIp(context),
            "本地数据库与数据目录初始化完成。",
            new
            {
                result.Engine,
                result.DataDirectory,
                result.Readable,
                result.Writable,
                items = result.Items.Select(item => new { item.Name, item.Status })
            });
        return Results.Ok(result);
    }

    private static async Task<IResult> SaveAsync(
        ServerSettingsUpdateRequest request,
        HttpContext context,
        IServerSettingsStore store,
        IPrivateNetworkValidator validator,
        IOptions<AppRuntimeOptions> options,
        IAuditLogService audit,
        CancellationToken cancellationToken)
    {
        await ValidateAsync(request, validator, options.Value.AllowPublicServerTargets, cancellationToken);
        await store.SaveAsync(request, cancellationToken);
        await audit.WriteAsync(
            "settings.update",
            "success",
            EndpointHelpers.RemoteIp(context),
            "服务器连接与存档设置已更新。",
            new
            {
                request.PalworldBaseUrl,
                request.PalworldUserName,
                request.PalDefenderBaseUrl,
                request.RconHost,
                request.RconPort,
                request.RconBase64,
                request.RconTimeoutSeconds,
                request.SaveWorldDirectory,
                request.SaveAutoIndex,
                request.SaveStableChecks,
                request.SaveStableCheckIntervalSeconds,
                request.SavePollIntervalSeconds,
                request.SaveMaximumFileSizeMb,
                request.BackupDirectory,
                request.BackupRetentionCount,
                request.BackupCompressionLevel,
                request.BackupExecuteSaveFirst,
                request.BackupRestoreEnabled,
                request.AutomationEnabled,
                request.AutomationPollIntervalSeconds,
                request.AutomationMaximumHistoryEntries
            },
            cancellationToken);
        return Results.Ok(await store.GetSummaryAsync(cancellationToken));
    }

    private static async Task<IResult> TestAsync(
        ConnectionTestRequest request,
        HttpContext context,
        IPalworldApiClient palworld,
        IPalDefenderApiClient palDefender,
        IServerSettingsStore store,
        IRconClient rcon,
        IPalworldSavDecompressor saveDecompressor,
        IPrivateNetworkValidator validator,
        IOptions<AppRuntimeOptions> options,
        IAuditLogService audit,
        CancellationToken cancellationToken)
    {
        var target = request.Target?.Trim().ToLowerInvariant() ?? string.Empty;
        if (request.Settings is not null)
        {
            await ValidateTargetAsync(target, request.Settings, validator, options.Value.AllowPublicServerTargets, cancellationToken);
        }

        var settings = await ResolveTestSettingsAsync(store, request.Settings, cancellationToken);
        object? details;
        switch (target)
        {
            case "palworld":
                details = new
                {
                    endpoint = ApiUriBuilder.Palworld(settings.Palworld.BaseUrl, "info").ToString(),
                    info = EndpointHelpers.LimitForAudit(
                        await palworld.GetInfoAsync(settings.Palworld, cancellationToken),
                        1000)
                };
                break;
            case "paldefender":
                details = new
                {
                    endpoint = ApiUriBuilder.PalDefender(settings.PalDefender.BaseUrl, "version").ToString(),
                    version = EndpointHelpers.LimitForAudit(
                        await palDefender.GetVersionAsync(settings.PalDefender, cancellationToken),
                        500)
                };
                break;
            case "rcon":
                const string probeCommand = "Info";
                var result = await rcon.ExecuteAsync(settings.Rcon, probeCommand, cancellationToken);
                details = new
                {
                    endpoint = $"{settings.Rcon.Host}:{settings.Rcon.Port}",
                    probeCommand,
                    response = EndpointHelpers.LimitForAudit(result.Response, 1000),
                    result.ElapsedMilliseconds
                };
                break;
            case "save":
                details = await TestSaveDirectoryAsync(settings.SaveGame, saveDecompressor, cancellationToken);
                break;
            default:
                throw new ArgumentException("Target 只能是 palworld、paldefender、rcon 或 save。", nameof(request));
        }

        await audit.WriteAsync(
            "settings.connection-test",
            "success",
            EndpointHelpers.RemoteIp(context),
            $"{target} 连接测试成功。",
            new { target, usedCurrentForm = request.Settings is not null },
            cancellationToken);
        return Results.Ok(new ConnectionTestResponse(true, "连接成功。", details));
    }

    private static async Task<object> TestSaveDirectoryAsync(
        SaveGameSettings save,
        IPalworldSavDecompressor decompressor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(save.WorldDirectory))
            throw new ArgumentException("尚未配置世界存档目录。", nameof(save));

        var worldDirectory = Path.GetFullPath(save.WorldDirectory);
        if (!Directory.Exists(worldDirectory))
            throw new DirectoryNotFoundException($"世界存档目录不存在：{worldDirectory}");

        var levelPath = Path.Combine(worldDirectory, "Level.sav");
        var playersPath = Path.Combine(worldDirectory, "Players");
        if (!File.Exists(levelPath))
            throw new FileNotFoundException("世界存档目录中不存在 Level.sav。", levelPath);
        if (!Directory.Exists(playersPath))
            throw new DirectoryNotFoundException($"世界存档目录中不存在 Players 子目录：{playersPath}");

        var info = new FileInfo(levelPath);
        var maximumBytes = checked((long)save.MaximumFileSizeMb * 1024L * 1024L);
        if (info.Length > maximumBytes)
            throw new InvalidOperationException($"Level.sav 大小 {info.Length:N0} 字节，超过配置上限 {maximumBytes:N0} 字节。");

        await using var stream = new FileStream(
            levelPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            true);
        var header = await decompressor.InspectAsync(stream, cancellationToken);

        return new
        {
            worldDirectory,
            levelPath,
            playersPath,
            levelSizeBytes = info.Length,
            levelModifiedUtc = info.LastWriteTimeUtc,
            playerSaveFiles = Directory.EnumerateFiles(playersPath, "*.sav", SearchOption.TopDirectoryOnly).Count(),
            readOnly = true,
            saveFormat = header.Format,
            saveType = header.SaveType.HasValue ? $"0x{header.SaveType.Value:X2}" : null,
            header.CnkWrapped,
            header.Supported,
            header.Message,
            header.HeaderHex,
            header.HeaderAscii,
            header.DeclaredCompressedLength,
            header.DeclaredUncompressedLength
        };
    }

    private static async Task<ServerSettings> ResolveTestSettingsAsync(
        IServerSettingsStore store,
        ServerSettingsUpdateRequest? current,
        CancellationToken cancellationToken)
    {
        var saved = await store.GetAsync(cancellationToken);
        if (current is null) return saved;

        return new ServerSettings(
            new PalworldConnection(
                NormalizeBaseUrl(current.PalworldBaseUrl),
                string.IsNullOrWhiteSpace(current.PalworldUserName) ? "admin" : current.PalworldUserName.Trim(),
                UseCurrentOrSaved(current.PalworldPassword, saved.Palworld.Password)),
            new PalDefenderConnection(
                NormalizeBaseUrl(current.PalDefenderBaseUrl),
                string.IsNullOrWhiteSpace(current.PalDefenderToken)
                    ? saved.PalDefender.Token
                    : CredentialInputNormalizer.NormalizePalDefenderToken(current.PalDefenderToken)),
            new RconConnection(
                current.RconHost.Trim(),
                current.RconPort,
                UseCurrentOrSaved(current.RconPassword, saved.Rcon.Password),
                current.RconBase64,
                current.RconTimeoutSeconds),
            new SaveGameSettings(
                current.SaveWorldDirectory.Trim(),
                current.SaveAutoIndex,
                current.SaveStableChecks,
                current.SaveStableCheckIntervalSeconds,
                current.SavePollIntervalSeconds,
                current.SaveMaximumFileSizeMb),
            new BackupSettings(
                current.BackupDirectory.Trim(),
                current.BackupRetentionCount,
                current.BackupCompressionLevel,
                current.BackupExecuteSaveFirst,
                current.BackupRestoreEnabled),
            new AutomationSettings(
                current.AutomationEnabled,
                current.AutomationPollIntervalSeconds,
                current.AutomationMaximumHistoryEntries));
    }

    private static string UseCurrentOrSaved(string? current, string saved)
        => string.IsNullOrWhiteSpace(current) ? saved : current;

    private static string NormalizeBaseUrl(string value) => value.Trim().TrimEnd('/');

    private static async Task ValidateTargetAsync(
        string target,
        ServerSettingsUpdateRequest request,
        IPrivateNetworkValidator validator,
        bool allowPublic,
        CancellationToken cancellationToken)
    {
        switch (target)
        {
            case "palworld":
                var palworld = await validator.ValidateHttpEndpointAsync(request.PalworldBaseUrl, allowPublic, cancellationToken);
                if (!palworld.IsValid) throw new ArgumentException($"Palworld API 地址无效：{palworld.Message}");
                ValidatePalworldUserName(request.PalworldUserName);
                break;
            case "paldefender":
                var palDefender = await validator.ValidateHttpEndpointAsync(request.PalDefenderBaseUrl, allowPublic, cancellationToken);
                if (!palDefender.IsValid) throw new ArgumentException($"PalDefender API 地址无效：{palDefender.Message}");
                break;
            case "rcon":
                await ValidateRconAsync(request, validator, allowPublic, cancellationToken);
                break;
            case "save":
                ValidateSaveSettings(request);
                break;
            default:
                throw new ArgumentException("Target 只能是 palworld、paldefender、rcon 或 save。", nameof(request));
        }
    }

    private static async Task ValidateAsync(
        ServerSettingsUpdateRequest request,
        IPrivateNetworkValidator validator,
        bool allowPublic,
        CancellationToken cancellationToken)
    {
        var palworld = await validator.ValidateHttpEndpointAsync(request.PalworldBaseUrl, allowPublic, cancellationToken);
        if (!palworld.IsValid) throw new ArgumentException($"Palworld API 地址无效：{palworld.Message}");

        var palDefender = await validator.ValidateHttpEndpointAsync(request.PalDefenderBaseUrl, allowPublic, cancellationToken);
        if (!palDefender.IsValid) throw new ArgumentException($"PalDefender API 地址无效：{palDefender.Message}");

        await ValidateRconAsync(request, validator, allowPublic, cancellationToken);
        ValidatePalworldUserName(request.PalworldUserName);
        ValidateSaveSettings(request);
    }

    private static async Task ValidateRconAsync(
        ServerSettingsUpdateRequest request,
        IPrivateNetworkValidator validator,
        bool allowPublic,
        CancellationToken cancellationToken)
    {
        var rcon = await validator.ValidateHostAsync(request.RconHost, allowPublic, cancellationToken);
        if (!rcon.IsValid) throw new ArgumentException($"RCON 主机无效：{rcon.Message}");
        if (request.RconPort is < 1 or > 65535) throw new ArgumentException("RCON 端口必须在 1 到 65535 之间。");
        if (request.RconTimeoutSeconds is < 3 or > 120) throw new ArgumentException("RCON 超时必须在 3 到 120 秒之间。");
    }

    private static void ValidatePalworldUserName(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName) || userName.Length > 128)
            throw new ArgumentException("Palworld API 用户名无效。");
    }

    private static void ValidateSaveSettings(ServerSettingsUpdateRequest request)
    {
        if (request.SaveStableChecks is < 2 or > 10)
            throw new ArgumentException("存档稳定检查次数必须在 2 到 10 之间。");
        if (request.SaveStableCheckIntervalSeconds is < 1 or > 30)
            throw new ArgumentException("存档稳定检查间隔必须在 1 到 30 秒之间。");
        if (request.SavePollIntervalSeconds is < 600 or > 3600)
            throw new ArgumentException("自动存档解析间隔必须在 600 到 3600 秒之间。");
        if (request.SaveMaximumFileSizeMb is < 128 or > 32768)
            throw new ArgumentException("Level.sav 最大文件限制必须在 128 到 32768 MB 之间。");
        if (request.SaveWorldDirectory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw new ArgumentException("世界存档目录包含无效字符。");
        if (request.BackupDirectory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw new ArgumentException("备份目录包含无效字符。");
        if (request.BackupRetentionCount is < 1 or > 500)
            throw new ArgumentException("备份保留数量必须在 1 到 500 之间。");
        if (request.BackupCompressionLevel is < 0 or > 9)
            throw new ArgumentException("备份压缩等级必须在 0 到 9 之间。");
        if (request.AutomationPollIntervalSeconds is < 5 or > 300)
            throw new ArgumentException("自动化轮询间隔必须在 5 到 300 秒之间。");
        if (request.AutomationMaximumHistoryEntries is < 20 or > 5000)
            throw new ArgumentException("自动化历史记录上限必须在 20 到 5000 之间。");
    }
}
