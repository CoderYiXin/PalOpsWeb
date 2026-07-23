using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;
using PalOps.Web.Contracts;
using PalOps.Web.Platform.Caching;

namespace PalOps.Web.Settings;

public interface IServerSettingsStore
{
    event EventHandler? Changed;

    Task<ServerSettings> GetAsync(CancellationToken cancellationToken = default);
    Task<ServerSettingsSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ServerSettingsUpdateRequest request, CancellationToken cancellationToken = default);
}

public sealed class ServerSettingsStore : IServerSettingsStore
{
    public event EventHandler? Changed;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly IDataProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IPlatformCache _cache;

    public ServerSettingsStore(
        IHostEnvironment environment,
        IOptions<AppRuntimeOptions> options,
        IDataProtectionProvider protectionProvider,
        IPlatformCache cache)
    {
        var configured = options.Value.DataDirectory;
        var directory = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "server-settings.json");
        _protector = protectionProvider.CreateProtector("PalOps.Web.ServerSettings.v1");
        _cache = cache;
    }

    public Task<ServerSettings> GetAsync(CancellationToken cancellationToken = default) =>
        _cache.GetOrCreateAsync(
            "settings:full",
            TimeSpan.FromMinutes(5),
            LoadSettingsAsync,
            ["settings"],
            cancellationToken);

    private async Task<ServerSettings> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        var stored = await ReadStoredAsync(cancellationToken) ?? StoredServerSettings.CreateDefault();
        return new ServerSettings(
            new PalworldConnection(
                stored.PalworldBaseUrl,
                stored.PalworldUserName,
                Unprotect(stored.ProtectedPalworldPassword)),
            new PalDefenderConnection(
                stored.PalDefenderBaseUrl,
                CredentialInputNormalizer.NormalizePalDefenderToken(Unprotect(stored.ProtectedPalDefenderToken))),
            new RconConnection(
                stored.RconHost,
                stored.RconPort,
                Unprotect(stored.ProtectedRconPassword),
                stored.RconBase64,
                NormalizeRange(stored.RconTimeoutSeconds, 3, 120, 31)),
            new SaveGameSettings(
                stored.SaveWorldDirectory?.Trim() ?? string.Empty,
                stored.SaveAutoIndex,
                NormalizeRange(stored.SaveStableChecks, 2, 10, 3),
                NormalizeRange(stored.SaveStableCheckIntervalSeconds, 1, 30, 2),
                NormalizeRange(stored.SavePollIntervalSeconds, 600, 3600, 600),
                NormalizeRange(stored.SaveMaximumFileSizeMb, 128, 32768, 8192)),
            new BackupSettings(
                stored.BackupDirectory?.Trim() ?? string.Empty,
                NormalizeRange(stored.BackupRetentionCount, 1, 500, 20),
                NormalizeRange(stored.BackupCompressionLevel, 0, 9, 6),
                stored.BackupExecuteSaveFirst,
                stored.BackupRestoreEnabled),
            new AutomationSettings(
                stored.AutomationEnabled,
                NormalizeRange(stored.AutomationPollIntervalSeconds, 5, 300, 15),
                NormalizeRange(stored.AutomationMaximumHistoryEntries, 20, 5000, 500)));
    }

    public Task<ServerSettingsSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default) =>
        _cache.GetOrCreateAsync(
            "settings:summary",
            TimeSpan.FromMinutes(5),
            LoadSummaryAsync,
            ["settings"],
            cancellationToken);

    private async Task<ServerSettingsSummaryResponse> LoadSummaryAsync(CancellationToken cancellationToken)
    {
        var settings = await GetAsync(cancellationToken);
        var stored = await ReadStoredAsync(cancellationToken) ?? StoredServerSettings.CreateDefault();
        return new ServerSettingsSummaryResponse(
            settings.Palworld.BaseUrl,
            settings.Palworld.UserName,
            !string.IsNullOrWhiteSpace(stored.ProtectedPalworldPassword),
            settings.PalDefender.BaseUrl,
            !string.IsNullOrWhiteSpace(stored.ProtectedPalDefenderToken),
            settings.Rcon.Host,
            settings.Rcon.Port,
            !string.IsNullOrWhiteSpace(stored.ProtectedRconPassword),
            settings.Rcon.Base64,
            settings.Rcon.TimeoutSeconds,
            settings.SaveGame.WorldDirectory,
            settings.SaveGame.AutoIndex,
            settings.SaveGame.StableChecks,
            settings.SaveGame.StableCheckIntervalSeconds,
            settings.SaveGame.PollIntervalSeconds,
            settings.SaveGame.MaximumFileSizeMb,
            settings.Backup.Directory,
            settings.Backup.RetentionCount,
            settings.Backup.CompressionLevel,
            settings.Backup.ExecuteSaveFirst,
            settings.Backup.RestoreEnabled,
            settings.Automation.Enabled,
            settings.Automation.PollIntervalSeconds,
            settings.Automation.MaximumHistoryEntries);
    }

    public async Task SaveAsync(ServerSettingsUpdateRequest request, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await ReadStoredWithoutLockAsync(cancellationToken) ?? StoredServerSettings.CreateDefault();
            var stored = new StoredServerSettings
            {
                PalworldBaseUrl = NormalizeBaseUrl(request.PalworldBaseUrl),
                PalworldUserName = string.IsNullOrWhiteSpace(request.PalworldUserName)
                    ? "admin"
                    : request.PalworldUserName.Trim(),
                ProtectedPalworldPassword = ProtectOrKeep(request.PalworldPassword, existing.ProtectedPalworldPassword),
                PalDefenderBaseUrl = NormalizeBaseUrl(request.PalDefenderBaseUrl),
                ProtectedPalDefenderToken = ProtectOrKeep(
                    CredentialInputNormalizer.NormalizePalDefenderToken(request.PalDefenderToken),
                    existing.ProtectedPalDefenderToken),
                RconHost = request.RconHost.Trim(),
                RconPort = request.RconPort,
                ProtectedRconPassword = ProtectOrKeep(request.RconPassword, existing.ProtectedRconPassword),
                RconBase64 = request.RconBase64,
                RconTimeoutSeconds = request.RconTimeoutSeconds,
                SaveWorldDirectory = request.SaveWorldDirectory.Trim(),
                SaveAutoIndex = request.SaveAutoIndex,
                SaveStableChecks = request.SaveStableChecks,
                SaveStableCheckIntervalSeconds = request.SaveStableCheckIntervalSeconds,
                SavePollIntervalSeconds = request.SavePollIntervalSeconds,
                SaveMaximumFileSizeMb = request.SaveMaximumFileSizeMb,
                BackupDirectory = request.BackupDirectory.Trim(),
                BackupRetentionCount = request.BackupRetentionCount,
                BackupCompressionLevel = request.BackupCompressionLevel,
                BackupExecuteSaveFirst = request.BackupExecuteSaveFirst,
                BackupRestoreEnabled = request.BackupRestoreEnabled,
                AutomationEnabled = request.AutomationEnabled,
                AutomationPollIntervalSeconds = request.AutomationPollIntervalSeconds,
                AutomationMaximumHistoryEntries = request.AutomationMaximumHistoryEntries
            };

            var temporaryPath = _path + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, stored, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _path, true);
            _cache.RemoveByTag("settings");
            _cache.RemoveByTag("readiness");
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<StoredServerSettings?> ReadStoredAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadStoredWithoutLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<StoredServerSettings?> ReadStoredWithoutLockAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return null;
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<StoredServerSettings>(stream, JsonOptions, cancellationToken);
    }

    private string ProtectOrKeep(string? plaintext, string existing)
        => string.IsNullOrWhiteSpace(plaintext) ? existing : _protector.Protect(plaintext);

    private string Unprotect(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue)) return string.Empty;
        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (Exception)
        {
            // A moved data directory can contain keys from another installation. Do not prevent startup;
            // the UI will show the credential as configured but the next connection test will request re-entry.
            return string.Empty;
        }
    }

    private static string NormalizeBaseUrl(string value) => value.Trim().TrimEnd('/');

    private static int NormalizeRange(int value, int minimum, int maximum, int fallback)
        => value >= minimum && value <= maximum ? value : fallback;

    private sealed class StoredServerSettings
    {
        public string PalworldBaseUrl { get; set; } = "http://127.0.0.1:8212";
        public string PalworldUserName { get; set; } = "admin";
        public string ProtectedPalworldPassword { get; set; } = string.Empty;
        public string PalDefenderBaseUrl { get; set; } = "http://127.0.0.1:17993";
        public string ProtectedPalDefenderToken { get; set; } = string.Empty;
        public string RconHost { get; set; } = "127.0.0.1";
        public int RconPort { get; set; } = 25575;
        public string ProtectedRconPassword { get; set; } = string.Empty;
        public bool RconBase64 { get; set; } = true;
        public int RconTimeoutSeconds { get; set; } = 31;
        public string SaveWorldDirectory { get; set; } = string.Empty;
        public bool SaveAutoIndex { get; set; } = true;
        public int SaveStableChecks { get; set; } = 3;
        public int SaveStableCheckIntervalSeconds { get; set; } = 2;
        public int SavePollIntervalSeconds { get; set; } = 600;
        public int SaveMaximumFileSizeMb { get; set; } = 8192;
        public string BackupDirectory { get; set; } = string.Empty;
        public int BackupRetentionCount { get; set; } = 20;
        public int BackupCompressionLevel { get; set; } = 6;
        public bool BackupExecuteSaveFirst { get; set; } = true;
        public bool BackupRestoreEnabled { get; set; }
        public bool AutomationEnabled { get; set; } = false;
        public int AutomationPollIntervalSeconds { get; set; } = 15;
        public int AutomationMaximumHistoryEntries { get; set; } = 500;

        public static StoredServerSettings CreateDefault() => new();
    }
}
