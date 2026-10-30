using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UptimeMonitor.Api.Configuration;
using UptimeMonitor.Api.Data;

namespace UptimeMonitor.Api.Services;

public sealed class MonitorWorker(IServiceScopeFactory scopeFactory, IOptions<MonitoringOptions> options,
    ILogger<MonitorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        var nextCleanup = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ids = await db.Monitors.AsNoTracking().Select(s => s.Id).ToArrayAsync(stoppingToken);
                await Parallel.ForEachAsync(ids, new ParallelOptions
                {
                    MaxDegreeOfParallelism = options.Value.MaxConcurrentChecks, CancellationToken = stoppingToken
                }, async (id, token) =>
                {
                    try
                    {
                        await using var checkScope = scopeFactory.CreateAsyncScope();
                        await checkScope.ServiceProvider.GetRequiredService<MonitorCheckService>().RunAsync(id, token, onlyIfDue: true);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception ex) { logger.LogError(ex, "Не удалось проверить сайт {MonitorId}.", id); }
                });

                if (DateTime.UtcNow >= nextCleanup)
                {
                    await scope.ServiceProvider.GetRequiredService<HistoryRetentionService>().CleanupAsync(stoppingToken);
                    nextCleanup = DateTime.UtcNow.AddHours(1);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Ошибка фонового мониторинга."); }

            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
