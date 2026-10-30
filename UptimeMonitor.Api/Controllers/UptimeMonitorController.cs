using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UptimeMonitor.Api.Configuration;
using UptimeMonitor.Api.Data;
using UptimeMonitor.Api.Models;
using UptimeMonitor.Api.Services;

namespace UptimeMonitor.Api.Controllers;

[ApiController]
[Route("api/monitors")]
public sealed class MonitorsController(AppDbContext db, MonitorCheckService checkService,
    MonitorLocks locks, IOptions<MonitoringOptions> options) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MonitoredSite[]>> Get(CancellationToken token,
        [FromQuery, StringLength(200)] string? name = null,
        [FromQuery, Range(1, 500)] int limit = 100,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0)
    {
        var query = db.Monitors.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(name)) query = query.Where(s => s.Name.Contains(name.Trim()));
        return Ok(await query.OrderBy(s => s.Name).ThenBy(s => s.Id).Skip(offset).Take(limit).ToArrayAsync(token));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MonitoredSite>> GetById(Guid id, CancellationToken token)
    {
        var site = await db.Monitors.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, token);
        return site is null ? NotFound() : Ok(site);
    }

    [HttpPost]
    public async Task<ActionResult<MonitoredSite>> Post(CreateMonitorRequest request, CancellationToken token)
    {
        await locks.Creation.WaitAsync(token);
        try
        {
            if (await db.Monitors.CountAsync(token) >= options.Value.MaxMonitors)
                return Problem(statusCode: 409, title: "Достигнут лимит мониторов");
            var site = new MonitoredSite
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Url = new Uri(request.Url).AbsoluteUri,
                CheckIntervalSeconds = request.CheckIntervalSeconds
            };
            db.Monitors.Add(site);
            await db.SaveChangesAsync(token);
            return CreatedAtAction(nameof(GetById), new { id = site.Id }, site);
        }
        finally { locks.Creation.Release(); }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateMonitorRequest request, CancellationToken token)
    {
        var gate = locks.For(id);
        await gate.WaitAsync(token);
        try
        {
            var site = await db.Monitors.FirstOrDefaultAsync(s => s.Id == id, token);
            if (site is null) return NotFound();
            var url = new Uri(request.Url).AbsoluteUri;
            if (site.Url != url && (await db.CheckResults.AnyAsync(c => c.MonitorId == id, token)
                || await db.Incidents.AnyAsync(i => i.MonitorId == id, token)))
                return Problem(statusCode: 409, title: "Для нового URL создайте отдельный монитор",
                    detail: "Это сохранит историю и инциденты привязанными к исходному сайту.");
            site.Name = request.Name.Trim();
            site.Url = url;
            site.CheckIntervalSeconds = request.CheckIntervalSeconds;
            await db.SaveChangesAsync(token);
            return NoContent();
        }
        finally { gate.Release(); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken token)
    {
        var gate = locks.For(id);
        await gate.WaitAsync(token);
        try
        {
            var site = await db.Monitors.FirstOrDefaultAsync(s => s.Id == id, token);
            if (site is null) return NotFound();
            db.Monitors.Remove(site);
            await db.SaveChangesAsync(token);
            return NoContent();
        }
        finally { gate.Release(); }
    }

    [HttpPost("{id:guid}/checks")]
    [EnableRateLimiting("checks")]
    public async Task<IActionResult> RunCheck(Guid id, CancellationToken token)
    {
        var result = await checkService.RunAsync(id, token);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/checks")]
    public async Task<IActionResult> GetChecks(Guid id, CancellationToken token,
        [FromQuery, Range(1, 200)] int limit = 50,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0)
    {
        if (!await db.Monitors.AnyAsync(s => s.Id == id, token)) return NotFound();
        return Ok(await db.CheckResults.AsNoTracking().Where(c => c.MonitorId == id)
            .OrderByDescending(c => c.CheckedAtUtc).ThenBy(c => c.Id)
            .Skip(offset).Take(limit).ToArrayAsync(token));
    }

    [HttpGet("{id:guid}/incidents")]
    public async Task<IActionResult> GetIncidents(Guid id, CancellationToken token,
        [FromQuery, Range(1, 200)] int limit = 50,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0)
    {
        if (!await db.Monitors.AnyAsync(s => s.Id == id, token)) return NotFound();
        return Ok(await db.Incidents.AsNoTracking().Where(i => i.MonitorId == id)
            .OrderByDescending(i => i.StartedAtUtc).ThenBy(i => i.Id)
            .Skip(offset).Take(limit).Select(i => new
            {
                i.Id,
                i.MonitorId,
                i.StartedAtUtc,
                i.ResolvedAtUtc,
                i.ErrorMessage,
                IsOpen = i.ResolvedAtUtc == null
            }).ToArrayAsync(token));
    }

    [HttpGet("{id:guid}/status")]
    public async Task<IActionResult> GetStatus(Guid id, CancellationToken token)
    {
        var monitor = await db.Monitors.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, token);
        if (monitor is null) return NotFound();
        var last = await db.CheckResults.AsNoTracking().Where(c => c.MonitorId == id)
            .OrderByDescending(c => c.CheckedAtUtc).ThenBy(c => c.Id).FirstOrDefaultAsync(token);
        return Ok(new
        {
            monitor.Id,
            monitor.Name,
            monitor.Url,
            monitor.CheckIntervalSeconds,
            Status = Status(last, monitor.CheckIntervalSeconds, DateTime.UtcNow),
            LastCheck = last
        });
    }

    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken token)
    {
        var now = DateTime.UtcNow;
        var since = now.AddHours(-24);
        var rows = await db.Monitors.AsNoTracking().OrderBy(s => s.Name).ThenBy(s => s.Id)
            .Select(s => new
            {
                Monitor = s,
                LastCheck = db.CheckResults.Where(c => c.MonitorId == s.Id)
                    .OrderByDescending(c => c.CheckedAtUtc).ThenBy(c => c.Id).FirstOrDefault()
            }).ToArrayAsync(token);
        var stats = await db.CheckResults.AsNoTracking().Where(c => c.CheckedAtUtc >= since)
            .GroupBy(c => c.MonitorId).Select(g => new
            {
                Id = g.Key,
                Total = g.Count(),
                Successful = g.Count(c => c.IsSuccessful),
                AverageResponseTimeMs = g.Where(c => c.IsSuccessful).Average(c => (double?)c.ResponseTimeMs)
            }).ToDictionaryAsync(s => s.Id, token);
        return Ok(new
        {
            GeneratedAtUtc = now,
            ObservationPoint = "Сервер мониторинга",
            RetentionDays = options.Value.RetentionDays,
            Monitors = rows.Select(row =>
            {
                stats.TryGetValue(row.Monitor.Id, out var stat);
                return new
                {
                    row.Monitor.Id,
                    row.Monitor.Name,
                    row.Monitor.Url,
                    row.Monitor.CheckIntervalSeconds,
                    Status = Status(row.LastCheck, row.Monitor.CheckIntervalSeconds, now),
                    row.LastCheck,
                    ChecksLast24Hours = stat?.Total ?? 0,
                    UptimePercent24Hours = stat is null ? (double?)null : Math.Round(100.0 * stat.Successful / stat.Total, 2),
                    AverageResponseTimeMs24Hours = stat?.AverageResponseTimeMs
                };
            })
        });
    }

    private string Status(CheckResult? last, int interval, DateTime now)
    {
        if (last is null) return "unknown";
        if (now - last.CheckedAtUtc > TimeSpan.FromSeconds(interval * 2 + options.Value.TimeoutSeconds + options.Value.PollIntervalSeconds))
            return "stale";
        return last.IsSuccessful ? "up" : "down";
    }
}
