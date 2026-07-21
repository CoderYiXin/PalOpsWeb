using System.Text.Json;
using PalOps.Web.Infrastructure;

namespace PalOps.Web.Maintenance;

public interface IMaintenanceRepository
{
    Task<CrashGuardConfiguration> GetCrashGuardConfigurationAsync(CancellationToken cancellationToken = default);
    Task SaveCrashGuardConfigurationAsync(CrashGuardConfiguration configuration, CancellationToken cancellationToken = default);
    Task<CrashGuardState> GetCrashGuardStateAsync(CancellationToken cancellationToken = default);
    Task SaveCrashGuardStateAsync(CrashGuardState state, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaintenancePlan>> ListPlansAsync(CancellationToken cancellationToken = default);
    Task<MaintenancePlan?> FindPlanAsync(string id, CancellationToken cancellationToken = default);
    Task UpsertPlanAsync(MaintenancePlan plan, CancellationToken cancellationToken = default);
    Task DeletePlanAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaintenanceRun>> ListRunsAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<MaintenanceRun?> FindRunAsync(string id, CancellationToken cancellationToken = default);
    Task UpsertRunAsync(MaintenanceRun run, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaintenanceCrashEvent>> ListCrashEventsAsync(int limit = 200, CancellationToken cancellationToken = default);
    Task AppendCrashEventAsync(MaintenanceCrashEvent crashEvent, CancellationToken cancellationToken = default);
}

public sealed class JsonMaintenanceRepository : IMaintenanceRepository
{
    private const int MaximumRuns = 500;
    private const int MaximumCrashEvents = 1000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<JsonMaintenanceRepository> _logger;

    public JsonMaintenanceRepository(IRuntimePathResolver paths, ILogger<JsonMaintenanceRepository> logger)
    {
        var directory = paths.ResolveDataPath("maintenance");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "state.json");
        _logger = logger;
    }

    public Task<CrashGuardConfiguration> GetCrashGuardConfigurationAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(document => document.CrashGuardConfiguration, cancellationToken);

    public Task SaveCrashGuardConfigurationAsync(CrashGuardConfiguration configuration, CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            var startsFreshWindow = configuration.Enabled && !document.CrashGuardConfiguration.Enabled;
            document.CrashGuardConfiguration = configuration;
            if (!startsFreshWindow) return;

            document.CrashGuardState = document.CrashGuardState with
            {
                LastResetAt = configuration.UpdatedAt,
                LastMessage = "崩溃守护已启用，崩溃统计窗口从当前时间重新开始。"
            };
        }, cancellationToken);

    public Task<CrashGuardState> GetCrashGuardStateAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(document => document.CrashGuardState, cancellationToken);

    public Task SaveCrashGuardStateAsync(CrashGuardState state, CancellationToken cancellationToken = default) =>
        MutateAsync(document => document.CrashGuardState = state, cancellationToken);

    public Task<IReadOnlyList<MaintenancePlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<MaintenancePlan>>(document => document.Plans
            .OrderByDescending(item => item.Enabled)
            .ThenBy(item => item.NextRunAt ?? DateTimeOffset.MaxValue)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray(), cancellationToken);

    public async Task<MaintenancePlan?> FindPlanAsync(string id, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeId(id);
        return await ReadAsync(document => document.Plans.FirstOrDefault(item =>
            item.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase)), cancellationToken);
    }

    public Task UpsertPlanAsync(MaintenancePlan plan, CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            var index = document.Plans.FindIndex(item => item.Id.Equals(plan.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) document.Plans[index] = plan;
            else document.Plans.Add(plan);
        }, cancellationToken);

    public Task DeletePlanAsync(string id, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeId(id);
        return MutateAsync(document => document.Plans.RemoveAll(item =>
            item.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase)), cancellationToken);
    }

    public Task<IReadOnlyList<MaintenanceRun>> ListRunsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, MaximumRuns);
        return ReadAsync<IReadOnlyList<MaintenanceRun>>(document => document.Runs
            .OrderByDescending(item => item.StartedAt)
            .Take(take)
            .ToArray(), cancellationToken);
    }

    public async Task<MaintenanceRun?> FindRunAsync(string id, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeId(id);
        return await ReadAsync(document => document.Runs.FirstOrDefault(item =>
            item.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase)), cancellationToken);
    }

    public Task UpsertRunAsync(MaintenanceRun run, CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            var index = document.Runs.FindIndex(item => item.Id.Equals(run.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) document.Runs[index] = run;
            else document.Runs.Add(run);
            document.Runs = document.Runs
                .OrderByDescending(item => item.StartedAt)
                .Take(MaximumRuns)
                .ToList();
        }, cancellationToken);

    public Task<IReadOnlyList<MaintenanceCrashEvent>> ListCrashEventsAsync(int limit = 200, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, MaximumCrashEvents);
        return ReadAsync<IReadOnlyList<MaintenanceCrashEvent>>(document => document.CrashEvents
            .OrderByDescending(item => item.OccurredAt)
            .Take(take)
            .ToArray(), cancellationToken);
    }

    public Task AppendCrashEventAsync(MaintenanceCrashEvent crashEvent, CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            document.CrashEvents.Add(crashEvent);
            document.CrashEvents = document.CrashEvents
                .OrderByDescending(item => item.OccurredAt)
                .Take(MaximumCrashEvents)
                .ToList();
        }, cancellationToken);

    private async Task<T> ReadAsync<T>(Func<MaintenanceStateDocument, T> reader, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadWithoutLockAsync(cancellationToken);
            return reader(document);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task MutateAsync(Action<MaintenanceStateDocument> mutation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadWithoutLockAsync(cancellationToken);
            mutation(document);
            await WriteWithoutLockAsync(document, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<MaintenanceStateDocument> ReadWithoutLockAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new MaintenanceStateDocument();
        try
        {
            await using var stream = new FileStream(
                _path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, true);
            var document = await JsonSerializer.DeserializeAsync<MaintenanceStateDocument>(stream, JsonOptions, cancellationToken)
                           ?? new MaintenanceStateDocument();
            document.Plans ??= [];
            document.Runs ??= [];
            document.CrashEvents ??= [];
            document.CrashGuardConfiguration ??= CrashGuardConfiguration.Default();
            document.CrashGuardState ??= CrashGuardState.Default();
            return document;
        }
        catch (JsonException ex)
        {
            var corrupt = _path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            try { File.Move(_path, corrupt, true); }
            catch (Exception moveException) { _logger.LogWarning(moveException, "Failed to quarantine corrupt maintenance state file."); }
            _logger.LogError(ex, "Maintenance state was corrupt and has been reset. Quarantine={CorruptPath}", corrupt);
            return new MaintenanceStateDocument();
        }
    }

    private async Task WriteWithoutLockAsync(MaintenanceStateDocument document, CancellationToken cancellationToken)
    {
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                             temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
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
                try { File.Delete(temporary); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete temporary maintenance state file {Path}.", temporary); }
            }
        }
    }

    private static string NormalizeId(string id)
    {
        var value = (id ?? string.Empty).Trim();
        if (value.Length is < 8 or > 64) throw new ArgumentException("维护对象标识无效。");
        return value;
    }
}
