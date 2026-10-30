using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UptimeMonitor.Api.Configuration;
using UptimeMonitor.Api.Data;

namespace UptimeMonitor.Api.Services;

public sealed class HistoryRetentionService(AppDbContext db, IOptions<MonitoringOptions> options)
{
    public async Task CleanupAsync(CancellationToken token)
    {
        var cutoff = DateTime.UtcNow.AddDays(-options.Value.RetentionDays);
        await db.CheckResults.Where(c => c.CheckedAtUtc < cutoff).ExecuteDeleteAsync(token);
        await db.Incidents.Where(i => i.ResolvedAtUtc != null && i.ResolvedAtUtc < cutoff).ExecuteDeleteAsync(token);
    }
}
