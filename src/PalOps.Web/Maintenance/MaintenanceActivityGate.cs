namespace PalOps.Web.Maintenance;

public interface IMaintenanceActivityGate
{
    bool IsBusy { get; }
    string? Owner { get; }
    IDisposable? TryAcquire(string owner);
}

public sealed class MaintenanceActivityGate : IMaintenanceActivityGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private string? _owner;

    public bool IsBusy => _gate.CurrentCount == 0;
    public string? Owner
    {
        get { lock (_sync) return _owner; }
    }

    public IDisposable? TryAcquire(string owner)
    {
        if (!_gate.Wait(0)) return null;
        lock (_sync) _owner = string.IsNullOrWhiteSpace(owner) ? "unknown" : owner.Trim();
        return new Lease(this);
    }

    private void Release()
    {
        lock (_sync) _owner = null;
        _gate.Release();
    }

    private sealed class Lease(MaintenanceActivityGate owner) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Release();
        }
    }
}
