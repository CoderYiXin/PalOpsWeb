using System.Security.Cryptography;
using System.Text;
using PalOps.Web.PalworldConfiguration;

namespace PalOps.Web.AdvancedOperations;

public interface IConfigurationVersionService
{
    Task<ConfigurationVersionDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<ConfigurationVersionSnapshot> CaptureAsync(ConfigurationVersionCreateRequest request, string actor, CancellationToken cancellationToken = default);
    Task<ConfigurationVersionDiff> DiffAsync(string fromId, string toId, CancellationToken cancellationToken = default);
    Task<ConfigurationVersionSnapshot> RestoreAsync(string id, ConfigurationVersionRestoreRequest request, string actor, string remoteIp, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public sealed class ConfigurationVersionService(
    IAdvancedOperationsRepository repository,
    IPalworldConfigurationService configuration,
    AdvancedOperationsValidator validator) : IConfigurationVersionService
{
    public async Task<ConfigurationVersionDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var current = await configuration.GetAsync(cancellationToken);
        var state = await repository.ReadAsync(cancellationToken);
        var currentHash = CombinedHash(current.RawContent, current.LaunchArguments);
        return new(
            currentHash,
            current.ConfigurationPath,
            state.ConfigurationVersions.OrderByDescending(static item => item.CreatedAt)
                .Select(item => ToView(item, currentHash)).ToArray(),
            DateTimeOffset.UtcNow);
    }

    public async Task<ConfigurationVersionSnapshot> CaptureAsync(ConfigurationVersionCreateRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var name = validator.ValidateName(request.Name, nameof(request.Name), 120);
        var note = validator.LimitText(request.Note, 1000);
        var current = await configuration.GetAsync(cancellationToken);
        if (!current.ConfigurationExists) throw new InvalidOperationException("Palworld configuration file does not exist.");
        var contentHash = CombinedHash(current.RawContent, current.LaunchArguments);
        var now = DateTimeOffset.UtcNow;
        return await repository.MutateAsync(state =>
        {
            var existing = state.ConfigurationVersions.FirstOrDefault(item => item.Sha256.Equals(contentHash, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return ToView(existing, contentHash);
            var stored = new StoredConfigurationVersion
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Note = note,
                Sha256 = contentHash,
                SizeBytes = Encoding.UTF8.GetByteCount(current.RawContent) + Encoding.UTF8.GetByteCount(current.LaunchArguments),
                CreatedAt = now,
                CreatedBy = actor,
                SourcePath = current.ConfigurationPath,
                RawContent = current.RawContent,
                LaunchArguments = current.LaunchArguments,
                Settings = current.Settings.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase)
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
        return new(from.Id, to.Id, from.Sha256.Equals(to.Sha256, StringComparison.OrdinalIgnoreCase), keys, from.LaunchArguments, to.LaunchArguments);
    }

    public async Task<ConfigurationVersionSnapshot> RestoreAsync(string id, ConfigurationVersionRestoreRequest request, string actor, string remoteIp, CancellationToken cancellationToken = default)
    {
        AdvancedOperationsValidator.RequireConfirmation(request.Confirmation, "RESTORE CONFIGURATION");
        var state = await repository.ReadAsync(cancellationToken);
        var stored = Find(state, id);
        var current = await configuration.GetAsync(cancellationToken);
        if (current.ConfigurationExists)
        {
            var currentHash = CombinedHash(current.RawContent, current.LaunchArguments);
            await repository.MutateAsync(document =>
            {
                if (document.ConfigurationVersions.Any(item => item.Sha256.Equals(currentHash, StringComparison.OrdinalIgnoreCase))) return true;
                var now = DateTimeOffset.UtcNow;
                document.ConfigurationVersions.Add(new StoredConfigurationVersion
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = $"恢复前自动快照 {now:yyyy-MM-dd HH:mm:ss}",
                    Note = $"由 {actor} 在恢复配置版本 {stored.Name} 前自动创建。",
                    Sha256 = currentHash,
                    SizeBytes = Encoding.UTF8.GetByteCount(current.RawContent) + Encoding.UTF8.GetByteCount(current.LaunchArguments),
                    CreatedAt = now,
                    CreatedBy = actor,
                    SourcePath = current.ConfigurationPath,
                    RawContent = current.RawContent,
                    LaunchArguments = current.LaunchArguments,
                    Settings = current.Settings.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                });
                return true;
            }, cancellationToken);
        }
        var saveRequest = new PalworldConfigurationSaveRequest(
            stored.RawContent,
            stored.LaunchArguments,
            current.Sha256,
            current.RuntimeConfigurationUpdatedAt,
            true);
        var result = await configuration.SaveAsync(saveRequest, request.Restart, actor, remoteIp, cancellationToken);
        return ToView(stored, CombinedHash(result.Configuration.RawContent, result.Configuration.LaunchArguments));
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

    private static StoredConfigurationVersion Find(AdvancedOperationsStateDocument state, string id) =>
        state.ConfigurationVersions.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException("Configuration version not found.");

    private static ConfigurationVersionSnapshot ToView(StoredConfigurationVersion stored, string currentHash) =>
        new(stored.Id, stored.Name, stored.Note, stored.Sha256, stored.SizeBytes, stored.CreatedAt, stored.CreatedBy,
            stored.Sha256.Equals(currentHash, StringComparison.OrdinalIgnoreCase), stored.SourcePath);

    private static string CombinedHash(string rawContent, string launchArguments)
    {
        var bytes = Encoding.UTF8.GetBytes(rawContent + "\n--PALOPS-LAUNCH-ARGUMENTS--\n" + launchArguments);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
