using PalOps.Web.Contracts;
using PalOps.Web.Platform.Workers;
using PalOps.Web.Settings;

namespace PalOps.Web.Platform.Readiness;

[Flags]
public enum OperationalCapability
{
    None = 0,
    PalworldRest = 1 << 0,
    PalDefender = 1 << 1,
    Rcon = 1 << 2,
    SaveDirectory = 1 << 3,
    BackupDirectory = 1 << 4,
    SaveAutoIndex = 1 << 5,
    Automation = 1 << 6,
    PlayerSources = PalworldRest | PalDefender,
    Core = PalworldRest | PalDefender | Rcon | SaveDirectory | BackupDirectory
}

public sealed record OperationalReadinessSnapshot(
    OperationalCapability Available,
    DateTimeOffset EvaluatedAt)
{
    public bool HasAll(OperationalCapability capabilities) =>
        capabilities == OperationalCapability.None || (Available & capabilities) == capabilities;

    public bool HasAny(OperationalCapability capabilities) =>
        capabilities != OperationalCapability.None && (Available & capabilities) != OperationalCapability.None;

    public bool HasAnyOperationalConfiguration => HasAny(OperationalCapability.Core);
}

public interface IOperationalReadinessGate
{
    Task<OperationalReadinessSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<OperationalReadinessSnapshot> WaitUntilReadyAsync(
        string? workerName,
        OperationalCapability allOf = OperationalCapability.None,
        OperationalCapability anyOf = OperationalCapability.None,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Keeps operational background workers dormant until the configuration they depend on exists.
/// Saving System Settings pulses the gate so eligible workers activate immediately without an app restart.
/// </summary>
public sealed class OperationalReadinessGate : IOperationalReadinessGate, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private readonly IServerSettingsStore _settingsStore;
    private readonly IBackgroundWorkerSupervisor _workerSupervisor;
    private TaskCompletionSource<bool> _changeSignal = CreateSignal();

    public OperationalReadinessGate(
        IServerSettingsStore settingsStore,
        IBackgroundWorkerSupervisor workerSupervisor)
    {
        _settingsStore = settingsStore;
        _workerSupervisor = workerSupervisor;
        settingsStore.Changed += HandleSettingsChanged;
    }

    public async Task<OperationalReadinessSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var summary = await _settingsStore.GetSummaryAsync(cancellationToken).ConfigureAwait(false);
        var available = ResolveCapabilities(summary);
        return new OperationalReadinessSnapshot(available, DateTimeOffset.UtcNow);
    }

    public async Task<OperationalReadinessSnapshot> WaitUntilReadyAsync(
        string? workerName,
        OperationalCapability allOf = OperationalCapability.None,
        OperationalCapability anyOf = OperationalCapability.None,
        CancellationToken cancellationToken = default)
    {
        if (allOf == OperationalCapability.None && anyOf == OperationalCapability.None)
            throw new ArgumentException("至少需要指定一个后台能力条件。", nameof(allOf));

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(workerName))
                _workerSupervisor.Heartbeat(workerName);

            var signal = Volatile.Read(ref _changeSignal).Task;
            var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (IsSatisfied(snapshot, allOf, anyOf)) return snapshot;

            var delay = Task.Delay(PollInterval, cancellationToken);
            var completed = await Task.WhenAny(signal, delay).ConfigureAwait(false);
            await completed.ConfigureAwait(false);
        }
    }

    public void Dispose() => _settingsStore.Changed -= HandleSettingsChanged;

    private static bool IsSatisfied(
        OperationalReadinessSnapshot snapshot,
        OperationalCapability allOf,
        OperationalCapability anyOf)
    {
        var allSatisfied = allOf == OperationalCapability.None || snapshot.HasAll(allOf);
        var anySatisfied = anyOf == OperationalCapability.None || snapshot.HasAny(anyOf);
        return allSatisfied && anySatisfied;
    }

    private static OperationalCapability ResolveCapabilities(ServerSettingsSummaryResponse summary)
    {
        var available = OperationalCapability.None;

        if (!string.IsNullOrWhiteSpace(summary.PalworldBaseUrl)
            && !string.IsNullOrWhiteSpace(summary.PalworldUserName)
            && summary.PalworldPasswordConfigured)
            available |= OperationalCapability.PalworldRest;

        if (!string.IsNullOrWhiteSpace(summary.PalDefenderBaseUrl)
            && summary.PalDefenderTokenConfigured)
            available |= OperationalCapability.PalDefender;

        if (!string.IsNullOrWhiteSpace(summary.RconHost)
            && summary.RconPort is > 0 and <= 65535
            && summary.RconPasswordConfigured)
            available |= OperationalCapability.Rcon;

        if (!string.IsNullOrWhiteSpace(summary.SaveWorldDirectory))
        {
            available |= OperationalCapability.SaveDirectory;
            if (summary.SaveAutoIndex)
                available |= OperationalCapability.SaveAutoIndex;
        }

        if (!string.IsNullOrWhiteSpace(summary.BackupDirectory))
            available |= OperationalCapability.BackupDirectory;

        if (summary.AutomationEnabled
            && (available & OperationalCapability.Core) != OperationalCapability.None)
            available |= OperationalCapability.Automation;

        return available;
    }

    private void HandleSettingsChanged(object? sender, EventArgs eventArgs)
    {
        var replacement = CreateSignal();
        var previous = Interlocked.Exchange(ref _changeSignal, replacement);
        previous.TrySetResult(true);
    }

    private static TaskCompletionSource<bool> CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
