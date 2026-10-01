using Microsoft.EntityFrameworkCore;
using UptimeMonitor.Api.Data;
using UptimeMonitor.Api.Models;

namespace UptimeMonitor.Api.Services
{
    public class MonitorCheckService
    {
        private readonly AppDbContext _db;
        private readonly WebsiteChecker _checker;

        private static readonly SemaphoreSlim CheckLock = new(1, 1);

        public MonitorCheckService(
            AppDbContext db,
            WebsiteChecker checker)
        {
            _db = db;
            _checker = checker;
        }

        public async Task<CheckResult?> RunAsync(
            Guid monitorId,
            CancellationToken cancellationToken)
        {
            await CheckLock.WaitAsync(cancellationToken);

            try
            {
                var monitor = await _db.Monitors
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        site => site.Id == monitorId,
                        cancellationToken);

                if (monitor is null)
                {
                    return null;
                }

                var result = await _checker.CheckAsync(
                    monitor, cancellationToken);

                var openIncident = await _db.Incidents
                    .FirstOrDefaultAsync(
                        incident => incident.MonitorId == monitorId
                            && incident.ResolvedAtUtc == null,
                        cancellationToken);

                if (!result.IsSuccessful && openIncident is null)
                {
                    var incident = new Incident
                    {
                        Id = Guid.NewGuid(),
                        MonitorId = monitorId,
                        StartedAtUtc = result.CheckedAtUtc,
                        ErrorMessage = result.ErrorMessage
                            ?? $"Сайт вернул HTTP {result.StatusCode}."
                    };

                    _db.Incidents.Add(incident);
                }
                else if (result.IsSuccessful && openIncident is not null)
                {
                    openIncident.ResolvedAtUtc = result.CheckedAtUtc;
                }

                _db.CheckResults.Add(result);
                await _db.SaveChangesAsync(cancellationToken);

                return result;
            }
            finally
            {
                CheckLock.Release();
            }
        }
    }
}