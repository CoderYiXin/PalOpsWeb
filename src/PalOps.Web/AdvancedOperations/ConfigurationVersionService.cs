using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PalOps.Web.PalworldConfiguration;
using PalOps.Web.Settings;

namespace PalOps.Web.AdvancedOperations;

public interface IConfigurationVersionService
{
    Task<ConfigurationVersionDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<ConfigurationVersionSnapshot> CaptureAsync(ConfigurationVersionCreateRequest request, string actor, CancellationToken cancellationToken = default);
    Task<ConfigurationVersionSnapshot?> CaptureAutomaticAsync(string sourceType, string actor, CancellationToken cancellationToken = default);
    Task<ConfigurationVersionDiff> DiffAsync(string fromId, string toId, CancellationToken cancellationToken = default);
    Task<ConfigurationVersionSnapshot> RestoreAsync(string id, ConfigurationVersionRestoreRequest request, string actor, string remoteIp, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public sealed class ConfigurationVersionService(
    IAdvancedOperationsRepository repository,
    IPalworldConfigurationService configuration,
    IServerSettingsStore settingsStore,
    AdvancedOperationsValidator validator,
    ILogger<ConfigurationVersionService> logger) : IConfigurationVersionService
{
    public async Task<ConfigurationVersionDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var current = await configuration.GetAsync(cancellationToken);
        var sanitized = await ReadSanitizedSettingsAsync(cancellationToken);
        var state = await repository.ReadAsync(cancellationToken);
        var currentHash = CombinedHash(current.RawContent, current.LaunchArguments, sanitized);
        return new(
            currentHash,
            current.ConfigurationPath,
            state.ConfigurationVersions.OrderByDescending(static item => item.CreatedAt)
                .Select(item => ToView(item, currentHash)).ToArray(),
            DateTimeOffset.UtcNow);
    }

    public async Task<ConfigurationVersionSnapshot> CaptureAsync(ConfigurationVersionCreateRequest request, string actor, CancellationToken cancellationToken = default) =>
        await CaptureCoreAsync(
            validator.ValidateName(request.Name, nameof(request.Name), 120),
            validator.LimitText(request.Note, 1000),
            "manual",
            actor,
            requireConfiguration: true,
            cancellationToken) ?? throw new InvalidOperationException("Configuration snapshot could not be created.");

    public async Task<ConfigurationVersionSnapshot?> CaptureAutomaticAsync(string sourceType, string actor, CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            return await CaptureCoreAsync(
                $"变更前自动快照 {now:yyyy-MM-dd HH:mm:ss}",
                "系统在配置写入前自动创建。",
                string.IsNullOrWhiteSpace(sourceType) ? "automatic" : sourceType.Trim(),
                actor,
                requireConfiguration: false,
                cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Automatic configuration snapshot could not be created for {SourceType}.", sourceType);
            return null;
        }
    }

    private async Task<ConfigurationVersionSnapshot?> CaptureCoreAsync(
        string name,
        string note,
        string sourceType,
        string actor,
        bool requireConfiguration,
        CancellationToken cancellationToken)
    {
        var current = await configuration.GetAsync(cancellationToken);
        if (!current.ConfigurationExists && requireConfiguration)
            throw new InvalidOperationException("Palworld configuration file does not exist.");
        var sanitized = await ReadSanitizedSettingsAsync(cancellationToken);
        var contentHash = CombinedHash(current.RawContent, current.LaunchArguments, sanitized);
        var now = DateTimeOffset.UtcNow;
        return await repository.MutateAsync(state =>
        {
            var existing = state.ConfigurationVersions.FirstOrDefault(item => item.Sha256.Equals(contentHash, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return ToView(existing, contentHash);
            var sections = new List<string> { "serverSettings" };
            if (current.ConfigurationExists) sections.Add("palworldConfiguration");
            var stored = new StoredConfigurationVersion
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Note = note,
                Sha256 = contentHash,
                SizeBytes = Encoding.UTF8.GetByteCount(current.RawContent) + Encoding.UTF8.GetByteCount(current.LaunchArguments) + Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(sanitized)),
                CreatedAt = now,
                CreatedBy = actor,
                SourcePath = current.ConfigurationPath,
                RawContent = current.RawContent,
                LaunchArguments = current.LaunchArguments,
                Settings = current.Settings.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase),
                SourceType = sourceType,
                Sections = sections,
                SanitizedSettings = new Dictionary<string, string>(sanitized, StringComparer.OrdinalIgnoreCase)
            };
            state.ConfigurationVersions.Add(stored);
            return ToView(stored, contentHash);
        }, cancellationToken);
    }

    public async Task<ConfigurationVersionDiff> DiffAsync(string fromId, string toId, CancellationToken cancellationToken = default)
    {
        var state = await repository.ReadAsync(cancellationToken);
        var from = Find(state, fromId);
        var to = Find(state, toId);
        var keys = from.Settings.Keys.Concat(to.Settings.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(key => !string.Equals(from.Settings.GetValueOrDefault(key), to.Settings.GetValueOrDefault(key), StringComparison.Ordinal))
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase).ToArray();
        var settingKeys = from.SanitizedSettings.Keys.Concat(to.SanitizedSettings.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(key => !string.Equals(from.SanitizedSettings.GetValueOrDefault(key), to.SanitizedSettings.GetValueOrDefault(key), StringComparison.Ordinal))
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase).ToArray();
        var changedSections = new List<string>();
        if (keys.Length > 0 || !string.Equals(from.RawContent, to.RawContent, StringComparison.Ordinal) || !string.Equals(from.LaunchArguments, to.LaunchArguments, StringComparison.Ordinal))
            changedSections.Add("palworldConfiguration");
        if (settingKeys.Length > 0) changedSections.Add("serverSettings");
        return new(from.Id, to.Id, from.Sha256.Equals(to.Sha256, StringComparison.OrdinalIgnoreCase), keys, from.LaunchArguments, to.LaunchArguments)
        {
            ChangedSections = changedSections,
            ChangedSettingKeys = settingKeys
        };
    }

    public async Task<ConfigurationVersionSnapshot> RestoreAsync(string id, ConfigurationVersionRestoreRequest request, string actor, string remoteIp, CancellationToken cancellationToken = default)
    {
        AdvancedOperationsValidator.RequireConfirmation(request.Confirmation, "RESTORE CONFIGURATION");
        var state = await repository.ReadAsync(cancellationToken);
        var stored = Find(state, id);
        // 恢复前自动快照必须在写入目标配置之前完成，确保误操作可回退。
        _ = await CaptureAutomaticAsync("pre-restore", actor, cancellationToken);
        var current = await configuration.GetAsync(cancellationToken);
        var saveRequest = new PalworldConfigurationSaveRequest(
            stored.RawContent,
            stored.LaunchArguments,
            current.Sha256,
            current.RuntimeConfigurationUpdatedAt,
            true);
        var result = await configuration.SaveAsync(saveRequest, request.Restart, actor, remoteIp, cancellationToken);
        var restoredAt = DateTimeOffset.UtcNow;
        await repository.MutateAsync(document =>
        {
            var target = Find(document, id);
            target.RestoredAt = restoredAt;
            target.RestoredBy = actor;
            return true;
        }, cancellationToken);
        var sanitized = await ReadSanitizedSettingsAsync(cancellationToken);
        stored.RestoredAt = restoredAt;
        stored.RestoredBy = actor;
        return ToView(stored, CombinedHash(result.Configuration.RawContent, result.Configuration.LaunchArguments, sanitized));
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        _ = await repository.MutateAsync(state =>
        {
            var removed = state.ConfigurationVersions.RemoveAll(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) throw new KeyNotFoundException("Configuration version not found.");
            return true;
        }, cancellationToken);
    }

    private async Task<Dictionary<string, string>> ReadSanitizedSettingsAsync(CancellationToken cancellationToken)
    {
        var summary = await settingsStore.GetSummaryAsync(cancellationToken);
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["palworld.baseUrl"] = summary.PalworldBaseUrl,
            ["palworld.userName"] = summary.PalworldUserName,
            ["palworld.passwordConfigured"] = summary.PalworldPasswordConfigured.ToString(),
            ["paldefender.baseUrl"] = summary.PalDefenderBaseUrl,
            ["paldefender.tokenConfigured"] = summary.PalDefenderTokenConfigured.ToString(),
            ["rcon.host"] = summary.RconHost,
            ["rcon.port"] = summary.RconPort.ToString(),
            ["rcon.passwordConfigured"] = summary.RconPasswordConfigured.ToString(),
            ["save.worldDirectory"] = summary.SaveWorldDirectory,
            ["backup.directory"] = summary.BackupDirectory,
            ["automation.enabled"] = summary.AutomationEnabled.ToString()
        };
    }

    private static StoredConfigurationVersion Find(AdvancedOperationsStateDocument state, string id) =>
        state.ConfigurationVersions.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException("Configuration version not found.");

    private static ConfigurationVersionSnapshot ToView(StoredConfigurationVersion stored, string currentHash) =>
        new(stored.Id, stored.Name, stored.Note, stored.Sha256, stored.SizeBytes, stored.CreatedAt, stored.CreatedBy,
            stored.Sha256.Equals(currentHash, StringComparison.OrdinalIgnoreCase), stored.SourcePath)
        {
            SourceType = stored.SourceType,
            Sections = stored.Sections,
            SanitizedSettings = stored.SanitizedSettings,
            RestoredAt = stored.RestoredAt,
            RestoredBy = stored.RestoredBy
        };

    private static string CombinedHash(string rawContent, string launchArguments, IReadOnlyDictionary<string, string> sanitizedSettings)
    {
        var normalizedSettings = string.Join("\n", sanitizedSettings.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(static pair => pair.Key + "=" + pair.Value));
        var bytes = Encoding.UTF8.GetBytes(rawContent + "\n--PALOPS-LAUNCH-ARGUMENTS--\n" + launchArguments + "\n--PALOPS-SANITIZED-SETTINGS--\n" + normalizedSettings);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
