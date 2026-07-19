using PalOps.Web.Contracts;
using PalOps.Web.Events;
using PalOps.Web.ServerRuntime;

namespace PalOps.Web.Notifications;

public interface INotificationAlertPolicyService
{
    ValueTask ObserveRuntimeAsync(PalServerRuntimeSnapshot snapshot, CancellationToken cancellationToken = default);
    ValueTask ObserveHealthAsync(IReadOnlyList<HealthComponentV1> components, CancellationToken cancellationToken = default);
}

public sealed class NotificationAlertPolicyService(
    IPalOpsEventPublisher publisher,
    ILogger<NotificationAlertPolicyService> logger) : INotificationAlertPolicyService
{
    private static readonly TimeSpan ReminderSilence = TimeSpan.FromHours(1);
    private readonly object _sync = new();
    private readonly Dictionary<string, AlertState> _states = new(StringComparer.OrdinalIgnoreCase);

    public async ValueTask ObserveRuntimeAsync(PalServerRuntimeSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot.Metrics is not { } metrics) return;

        await ObserveThresholdAsync(
            "system.cpu",
            metrics.SystemCpuPercent >= 90,
            metrics.SystemCpuPercent < 80,
            "system.cpu.high",
            "system.cpu.recovered",
            "系统 CPU 占用持续过高。",
            "系统 CPU 占用已恢复。",
            metrics.SystemCpuPercent,
            new Dictionary<string, object?> { ["cpuPercent"] = metrics.SystemCpuPercent },
            cancellationToken);

        await ObserveThresholdAsync(
            "system.memory",
            metrics.SystemMemoryPercent >= 90,
            metrics.SystemMemoryPercent < 80,
            "system.memory.high",
            "system.memory.recovered",
            "系统内存占用持续过高。",
            "系统内存占用已恢复。",
            metrics.SystemMemoryPercent,
            new Dictionary<string, object?>
            {
                ["memoryPercent"] = metrics.SystemMemoryPercent,
                ["memoryUsedBytes"] = metrics.SystemMemoryUsedBytes,
                ["memoryTotalBytes"] = metrics.SystemMemoryTotalBytes
            },
            cancellationToken);

        if (metrics.DiskTotalBytes > 0)
        {
            var freePercent = metrics.DiskFreeBytes * 100d / metrics.DiskTotalBytes;
            const long tenGiB = 10L * 1024 * 1024 * 1024;
            const long recoveryGiB = 12L * 1024 * 1024 * 1024;
            await ObserveThresholdAsync(
                "system.disk",
                metrics.DiskFreeBytes <= tenGiB || freePercent <= 10,
                metrics.DiskFreeBytes > recoveryGiB && freePercent > 12,
                "system.disk.low",
                "system.disk.recovered",
                "存档磁盘剩余空间不足。",
                "存档磁盘剩余空间已恢复。",
                freePercent,
                new Dictionary<string, object?>
                {
                    ["diskFreeBytes"] = metrics.DiskFreeBytes,
                    ["diskTotalBytes"] = metrics.DiskTotalBytes,
                    ["diskFreePercent"] = freePercent
                },
                cancellationToken);
        }

        // A real Server FPS source is not present in the current runtime snapshot.
        // Do not infer FPS from CPU or process metrics; the alert remains disabled until
        // an authoritative value is added to PalServerRuntimeSnapshot.
    }

    public async ValueTask ObserveHealthAsync(IReadOnlyList<HealthComponentV1> components, CancellationToken cancellationToken = default)
    {
        foreach (var component in components)
        {
            if (component.Status.Equals("notConfigured", StringComparison.OrdinalIgnoreCase)) continue;
            var healthy = component.Status.Equals("healthy", StringComparison.OrdinalIgnoreCase);
            await ObserveThresholdAsync(
                "component." + component.Name,
                !healthy,
                healthy,
                "component.unhealthy",
                "component.recovered",
                $"组件 {component.Name} 连续健康检查失败。",
                $"组件 {component.Name} 已恢复。",
                healthy ? 1 : 0,
                new Dictionary<string, object?>
                {
                    ["component"] = component.Name,
                    ["status"] = component.Status,
                    ["latencyMs"] = component.LatencyMs,
                    ["detail"] = component.Message
                },
                cancellationToken);
        }
    }

    private async ValueTask ObserveThresholdAsync(
        string key,
        bool warning,
        bool recovered,
        string warningEvent,
        string recoveryEvent,
        string warningMessage,
        string recoveryMessage,
        double value,
        IReadOnlyDictionary<string, object?> system,
        CancellationToken cancellationToken)
    {
        PalOpsEvent? eventToPublish = null;
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            if (!_states.TryGetValue(key, out var state))
            {
                state = new AlertState();
                _states[key] = state;
            }
            state.LastValue = value;

            if (state.Active)
            {
                if (recovered)
                {
                    state.Active = false;
                    state.Consecutive = 0;
                    state.LastNotifiedAt = now;
                    eventToPublish = PalOpsEvent.Create(
                        recoveryEvent,
                        "information",
                        system: system,
                        metadata: new Dictionary<string, object?> { ["message"] = recoveryMessage, ["alertKey"] = key });
                }
                else if (warning && (!state.LastNotifiedAt.HasValue || now - state.LastNotifiedAt.Value >= ReminderSilence))
                {
                    state.LastNotifiedAt = now;
                    eventToPublish = PalOpsEvent.Create(
                        warningEvent,
                        "warning",
                        system: system,
                        metadata: new Dictionary<string, object?> { ["message"] = warningMessage, ["alertKey"] = key, ["reminder"] = true });
                }
            }
            else if (!warning)
            {
                state.Consecutive = 0;
            }
            else
            {
                state.Consecutive++;
                if (state.Consecutive >= 3)
                {
                    state.Active = true;
                    state.LastNotifiedAt = now;
                    eventToPublish = PalOpsEvent.Create(
                        warningEvent,
                        "warning",
                        system: system,
                        metadata: new Dictionary<string, object?> { ["message"] = warningMessage, ["alertKey"] = key });
                }
            }
        }

        if (eventToPublish is null) return;
        try { await publisher.PublishAsync(eventToPublish, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to publish alert event {EventType}.", eventToPublish.EventType);
        }
    }

    private sealed class AlertState
    {
        public int Consecutive { get; set; }
        public bool Active { get; set; }
        public DateTimeOffset? LastNotifiedAt { get; set; }
        public double LastValue { get; set; }
    }
}
