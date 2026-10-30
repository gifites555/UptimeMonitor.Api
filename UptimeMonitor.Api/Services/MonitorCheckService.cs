using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UptimeMonitor.Api.Configuration;
using UptimeMonitor.Api.Data;
using UptimeMonitor.Api.Models;

namespace UptimeMonitor.Api.Services;

public sealed class MonitorCheckService(AppDbContext db, WebsiteChecker checker,
    MonitorLocks locks, CheckConcurrency concurrency, IOptions<MonitoringOptions> options)
{
    public async Task<CheckResult?> RunAsync(Guid monitorId, CancellationToken cancellationToken, bool onlyIfDue = false)
    {
        var gate = locks.For(monitorId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var monitor = await db.Monitors.AsNoTracking().FirstOrDefaultAsync(s => s.Id == monitorId, cancellationToken);
            if (monitor is null) return null;

            var lastCheckAt = await db.CheckResults.Where(c => c.MonitorId == monitorId)
                .MaxAsync(c => (DateTime?)c.CheckedAtUtc, cancellationToken);
            // Recheck under the lock: a manual check may have run while the worker waited.
            if (onlyIfDue && lastCheckAt.HasValue
                && DateTime.UtcNow < lastCheckAt.Value.AddSeconds(monitor.CheckIntervalSeconds)) return null;

            await concurrency.Gate.WaitAsync(cancellationToken);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
                CheckResult result;
                try { result = await checker.CheckAsync(monitor, timeout.Token); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    result = new CheckResult
                    {
                        Id = Guid.NewGuid(),
                        MonitorId = monitorId,
                        CheckedAtUtc = DateTime.UtcNow,
                        ResponseTimeMs = options.Value.TimeoutSeconds * 1000L,
                        ErrorMessage = "Сайт не ответил за отведённое время."
                    };
                }

                var openIncident = await db.Incidents.FirstOrDefaultAsync(
                    i => i.MonitorId == monitorId && i.ResolvedAtUtc == null, cancellationToken);
                if (!result.IsSuccessful && openIncident is null)
                    db.Incidents.Add(new Incident
                    {
                        Id = Guid.NewGuid(),
                        MonitorId = monitorId,
                        StartedAtUtc = result.CheckedAtUtc,
                        ErrorMessage = result.ErrorMessage
                    });
                else if (result.IsSuccessful && openIncident is not null)
                    openIncident.ResolvedAtUtc = result.CheckedAtUtc;

                db.CheckResults.Add(result);
                await db.SaveChangesAsync(cancellationToken);
                return result;
            }
            finally { concurrency.Gate.Release(); }
        }
        finally { gate.Release(); }
    }
}
