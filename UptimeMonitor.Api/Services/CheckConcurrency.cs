using Microsoft.Extensions.Options;
using UptimeMonitor.Api.Configuration;

namespace UptimeMonitor.Api.Services;

public sealed class CheckConcurrency(IOptions<MonitoringOptions> options) : IDisposable
{
    public SemaphoreSlim Gate { get; } = new(options.Value.MaxConcurrentChecks);
    public void Dispose() => Gate.Dispose();
}
