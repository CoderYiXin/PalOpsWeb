using System.Threading.Channels;

namespace PalOps.Web.Events;

public sealed class PalOpsEventBus(ILogger<PalOpsEventBus> logger) : IPalOpsEventBus
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Subscription> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

    public IPalOpsEventSubscription Subscribe(string subscriberName, int capacity = 1000)
    {
        if (string.IsNullOrWhiteSpace(subscriberName)) throw new ArgumentException("订阅名称不能为空。", nameof(subscriberName));
        capacity = Math.Clamp(capacity, 10, 10_000);
        var id = $"{subscriberName.Trim()}:{Guid.NewGuid():N}";
        var channel = Channel.CreateBounded<PalOpsEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        var subscription = new Subscription(id, subscriberName.Trim(), channel, Remove);
        lock (_sync) _subscriptions[id] = subscription;
        return subscription;
    }

    public ValueTask PublishAsync(PalOpsEvent palOpsEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(palOpsEvent);
        cancellationToken.ThrowIfCancellationRequested();
        Subscription[] subscriptions;
        lock (_sync) subscriptions = _subscriptions.Values.ToArray();
        foreach (var subscription in subscriptions)
        {
            if (!subscription.TryWrite(palOpsEvent))
                logger.LogWarning("Realtime event queue {Subscriber} rejected event {EventType}.", subscription.Name, palOpsEvent.EventType);
        }
        return ValueTask.CompletedTask;
    }

    private void Remove(string id)
    {
        Subscription? removed;
        lock (_sync)
        {
            _subscriptions.Remove(id, out removed);
        }
        removed?.Complete();
    }

    private sealed class Subscription(
        string id,
        string name,
        Channel<PalOpsEvent> channel,
        Action<string> remove) : IPalOpsEventSubscription
    {
        private int _disposed;
        public string Name { get; } = name;
        public bool TryWrite(PalOpsEvent value) => Volatile.Read(ref _disposed) == 0 && channel.Writer.TryWrite(value);
        public IAsyncEnumerable<PalOpsEvent> ReadAllAsync(CancellationToken cancellationToken = default) =>
            channel.Reader.ReadAllAsync(cancellationToken);
        public void Complete() => channel.Writer.TryComplete();
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) remove(id);
            return ValueTask.CompletedTask;
        }
    }
}
