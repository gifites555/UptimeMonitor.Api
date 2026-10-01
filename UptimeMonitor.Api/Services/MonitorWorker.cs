using Microsoft.EntityFrameworkCore;
using UptimeMonitor.Api.Data;

namespace UptimeMonitor.Api.Services
{
    public class MonitorWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MonitorWorker> _logger;

        public MonitorWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<MonitorWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await CheckMonitorsAsync(stoppingToken);
                    }
                    catch (OperationCanceledException)
                        when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex, "Ошибка фонового обхода сайтов.");
                    }

                    await Task.Delay(
                        TimeSpan.FromSeconds(5),
                        stoppingToken);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                // Приложение завершает работу.
            }
        }

        private async Task CheckMonitorsAsync(
            CancellationToken cancellationToken)
        {
            Guid[] monitorIds;

            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider
                    .GetRequiredService<AppDbContext>();

                monitorIds = await db.Monitors
                    .Select(site => site.Id)
                    .ToArrayAsync(cancellationToken);
            }

            foreach (var id in monitorIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await CheckIfDueAsync(id, cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex, "Не удалось проверить сайт {MonitorId}.", id);
                }
            }
        }

        private async Task CheckIfDueAsync(
    Guid id,
    CancellationToken cancellationToken)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var checkService = scope.ServiceProvider
                .GetRequiredService<MonitorCheckService>();

            var monitor = await db.Monitors
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    site => site.Id == id,
                    cancellationToken);

            if (monitor is null)
            {
                return;
            }

            var lastCheckAt = await db.CheckResults
                .Where(check => check.MonitorId == id)
                .MaxAsync(
                    check => (DateTime?)check.CheckedAtUtc,
                    cancellationToken);

            if (lastCheckAt.HasValue)
            {
                var nextCheckAt = lastCheckAt.Value
                    .AddSeconds(monitor.CheckIntervalSeconds);

                if (DateTime.UtcNow < nextCheckAt)
                {
                    return;
                }
            }

            var result = await checkService.RunAsync(
                id, cancellationToken);

            if (result is null)
            {
                return;
            }

            _logger.LogInformation(
                "Сайт {MonitorId} проверен. Успешно: {Success}",
                id,
                result.IsSuccessful);
        }
    }
}