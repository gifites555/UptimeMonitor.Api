namespace UptimeMonitor.Api.Services;

// Bounded striped locks share synchronization between checks, edits and deletes.
// One application instance owns a database; distributed scheduling is not supported.
public sealed class MonitorLocks : IDisposable
{
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 256).Select(_ => new SemaphoreSlim(1)).ToArray();
    public SemaphoreSlim For(Guid id) => _locks[(uint)id.GetHashCode() % (uint)_locks.Length];
    public SemaphoreSlim Creation { get; } = new(1);
    public void Dispose()
    {
        foreach (var gate in _locks) gate.Dispose();
        Creation.Dispose();
    }
}
