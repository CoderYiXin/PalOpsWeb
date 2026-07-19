namespace PalOps.Web.Events;

public sealed record PalOpsEvent(
    string EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    string Severity,
    IReadOnlyDictionary<string, object?> Server,
    IReadOnlyDictionary<string, object?> Player,
    IReadOnlyDictionary<string, object?> Backup,
    IReadOnlyDictionary<string, object?> System,
    IReadOnlyDictionary<string, object?> Metadata)
{
    private static readonly IReadOnlyDictionary<string, object?> Empty = new Dictionary<string, object?>();

    public static PalOpsEvent Create(
        string eventType,
        string severity = "information",
        IReadOnlyDictionary<string, object?>? server = null,
        IReadOnlyDictionary<string, object?>? player = null,
        IReadOnlyDictionary<string, object?>? backup = null,
        IReadOnlyDictionary<string, object?>? system = null,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(eventType)) throw new ArgumentException("事件类型不能为空。", nameof(eventType));
        return new(
            Guid.NewGuid().ToString("N"),
            eventType.Trim().ToLowerInvariant(),
            DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(severity) ? "information" : severity.Trim().ToLowerInvariant(),
            server ?? Empty,
            player ?? Empty,
            backup ?? Empty,
            system ?? Empty,
            metadata ?? Empty);
    }
}

public interface IPalOpsEventPublisher
{
    ValueTask PublishAsync(PalOpsEvent palOpsEvent, CancellationToken cancellationToken = default);
}

public interface IPalOpsEventSubscription : IAsyncDisposable
{
    IAsyncEnumerable<PalOpsEvent> ReadAllAsync(CancellationToken cancellationToken = default);
}

public interface IPalOpsEventBus : IPalOpsEventPublisher
{
    IPalOpsEventSubscription Subscribe(string subscriberName, int capacity = 1000);
}
