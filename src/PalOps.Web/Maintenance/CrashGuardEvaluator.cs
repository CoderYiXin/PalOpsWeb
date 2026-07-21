namespace PalOps.Web.Maintenance;

public sealed record CrashGuardEvaluation(
    int CrashesInWindow,
    DateTimeOffset WindowStartedAt,
    bool ThresholdReached,
    string Status,
    DateTimeOffset? NextEligibleRestartAt);

public sealed class CrashGuardEvaluator
{
    public CrashGuardEvaluation Evaluate(
        CrashGuardConfiguration configuration,
        CrashGuardState state,
        IReadOnlyList<MaintenanceCrashEvent> events,
        DateTimeOffset now)
    {
        var configuredWindowStart = now.AddMinutes(-configuration.WindowMinutes);
        var effectiveWindowStart = state.LastResetAt.HasValue && state.LastResetAt.Value > configuredWindowStart
            ? state.LastResetAt.Value
            : configuredWindowStart;
        var crashes = events.Count(item =>
            item.EventType.Equals("unexpected-exit", StringComparison.OrdinalIgnoreCase) &&
            item.OccurredAt >= effectiveWindowStart);
        var thresholdReached = crashes >= configuration.MaximumCrashes;
        var status = !configuration.Enabled ? "disabled" :
            state.Suspended ? "suspended" :
            state.CircuitOpen || thresholdReached ? "circuit-open" :
            "armed";
        var nextEligible = state.LastCrashAt?.AddSeconds(configuration.RestartDelaySeconds);
        return new CrashGuardEvaluation(crashes, effectiveWindowStart, thresholdReached, status, nextEligible);
    }
}
