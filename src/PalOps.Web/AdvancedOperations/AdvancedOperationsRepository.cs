using System.Text.Json;
using PalOps.Web.Infrastructure;

namespace PalOps.Web.AdvancedOperations;

public interface IAdvancedOperationsRepository
{
    Task<AdvancedOperationsStateDocument> ReadAsync(CancellationToken cancellationToken = default);
    Task<T> MutateAsync<T>(Func<AdvancedOperationsStateDocument, T> mutation, CancellationToken cancellationToken = default);
}

public sealed class JsonAdvancedOperationsRepository : IAdvancedOperationsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<JsonAdvancedOperationsRepository> _logger;
    private AdvancedOperationsStateDocument? _cached;

    public JsonAdvancedOperationsRepository(IRuntimePathResolver paths, ILogger<JsonAdvancedOperationsRepository> logger)
    {
        var directory = paths.ResolveDataPath("advanced-operations");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "state.json");
        _logger = logger;
    }

    public async Task<AdvancedOperationsStateDocument> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await GetCachedWithoutLockAsync(cancellationToken);
            return Clone(document);
        }
        finally { _gate.Release(); }
    }

    public async Task<T> MutateAsync<T>(Func<AdvancedOperationsStateDocument, T> mutation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await GetCachedWithoutLockAsync(cancellationToken);
            var document = Clone(current);
            var result = mutation(document);
            Normalize(document);
            await WriteWithoutLockAsync(document, cancellationToken);
            _cached = Clone(document);
            return result;
        }
        finally { _gate.Release(); }
    }

    private async Task<AdvancedOperationsStateDocument> GetCachedWithoutLockAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null) return _cached;
        _cached = await ReadFileWithoutLockAsync(cancellationToken);
        Normalize(_cached);
        return _cached;
    }

    private async Task<AdvancedOperationsStateDocument> ReadFileWithoutLockAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new AdvancedOperationsStateDocument();
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
            return await JsonSerializer.DeserializeAsync<AdvancedOperationsStateDocument>(stream, JsonOptions, cancellationToken)
                   ?? new AdvancedOperationsStateDocument();
        }
        catch (JsonException ex)
        {
            var quarantinePath = _path + $".corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
            try { File.Move(_path, quarantinePath, false); }
            catch (Exception moveException) { _logger.LogWarning(moveException, "Unable to quarantine invalid advanced operations state file."); }
            _logger.LogError(ex, "Advanced operations state is invalid; the invalid file was quarantined as {QuarantinePath}.", quarantinePath);
            return new AdvancedOperationsStateDocument();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Unable to read advanced operations state; the in-memory default state will be used for this process.");
            return new AdvancedOperationsStateDocument();
        }
    }

    private async Task WriteWithoutLockAsync(AdvancedOperationsStateDocument document, CancellationToken cancellationToken)
    {
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }
            File.Move(temporary, _path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); } catch { }
            }
        }
    }

    private static AdvancedOperationsStateDocument Clone(AdvancedOperationsStateDocument document) =>
        JsonSerializer.Deserialize<AdvancedOperationsStateDocument>(JsonSerializer.Serialize(document, JsonOptions), JsonOptions)
        ?? new AdvancedOperationsStateDocument();

    private static void Normalize(AdvancedOperationsStateDocument document)
    {
        document.SchemaVersion = 1;
        document.Incidents ??= [];
        document.IncidentRules ??= [];
        document.PlayerNotes = new Dictionary<string, PlayerInsightNote>(document.PlayerNotes ?? new Dictionary<string, PlayerInsightNote>(), StringComparer.OrdinalIgnoreCase);
        document.GovernanceReviews = new Dictionary<string, GovernanceReview>(document.GovernanceReviews ?? new Dictionary<string, GovernanceReview>(), StringComparer.OrdinalIgnoreCase);
        document.DisasterRecoveryTargets ??= [];
        document.DisasterRecoveryDrills ??= [];
        document.UpdatePlans ??= [];
        document.ConfigurationVersions ??= [];
        document.Playbooks ??= [];
        document.PlaybookRuns ??= [];
        document.SecurityPolicy ??= SecurityPolicy.Default();
        document.ApiTokens ??= [];
        document.SecurityObservations ??= [];
        document.IntegrationSubscriptions ??= [];
        document.IntegrationEvents ??= [];

        document.Incidents = document.Incidents.OrderByDescending(static item => item.UpdatedAt).Take(2000).ToList();
        document.DisasterRecoveryDrills = document.DisasterRecoveryDrills.OrderByDescending(static item => item.StartedAt).Take(500).ToList();
        document.UpdatePlans = document.UpdatePlans.OrderByDescending(static item => item.UpdatedAt).Take(500).ToList();
        document.ConfigurationVersions = document.ConfigurationVersions.OrderByDescending(static item => item.CreatedAt).Take(200).ToList();
        document.PlaybookRuns = document.PlaybookRuns.OrderByDescending(static item => item.StartedAt).Take(1000).ToList();
        document.SecurityObservations = document.SecurityObservations.OrderByDescending(static item => item.OccurredAt).Take(2000).ToList();
        document.IntegrationEvents = document.IntegrationEvents.OrderByDescending(static item => item.ReceivedAt).Take(2000).ToList();
    }
}
