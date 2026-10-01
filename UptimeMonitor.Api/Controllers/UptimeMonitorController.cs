using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UptimeMonitor.Api.Data;
using UptimeMonitor.Api.Models;
using UptimeMonitor.Api.Services;

namespace UptimeMonitor.Api.Controllers
{
    [ApiController]
    [Route("api/monitors")]
    public class MonitorsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly MonitorCheckService _checkService;

        public MonitorsController(
            AppDbContext db,
            MonitorCheckService checkService)
        {
            _db = db;
            _checkService = checkService;
        }

        // Все сайты или поиск по имени
        [HttpGet]
        public async Task<ActionResult<MonitoredSite[]>> Get(
            [FromQuery] string? name = null)
        {
            var query = _db.Monitors.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(name))
            {
                var search = name.Trim();

                query = query.Where(site =>
                    site.Name.Contains(search));
            }

            var sites = await query.ToArrayAsync();

            return Ok(sites);
        }

        // Получение по ID
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<MonitoredSite>> GetById(Guid id)
        {
            var site = await _db.Monitors
                .AsNoTracking()
                .FirstOrDefaultAsync(site => site.Id == id);

            if (site is null)
            {
                return NotFound();
            }

            return Ok(site);
        }

        // Создание
        [HttpPost]
        public async Task<ActionResult<MonitoredSite>> Post(
            [FromBody] CreateMonitorRequest request)
        {
            var site = new MonitoredSite
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Url = request.Url,
                CheckIntervalSeconds = request.CheckIntervalSeconds
            };

            _db.Monitors.Add(site);
            await _db.SaveChangesAsync();

            return CreatedAtAction(
                nameof(GetById),
                new { id = site.Id },
                site);
        }

        // Изменение
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(
            Guid id,
            [FromBody] UpdateMonitorRequest request)
        {
            var site = await _db.Monitors.FindAsync(id);

            if (site is null)
            {
                return NotFound();
            }

            site.Name = request.Name;
            site.Url = request.Url;
            site.CheckIntervalSeconds = request.CheckIntervalSeconds;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict();
            }

            return NoContent();
        }

        // Удаление
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var site = await _db.Monitors.FindAsync(id);

            if (site is null)
            {
                return NotFound();
            }

            _db.Monitors.Remove(site);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict();
            }

            return NoContent();
        }

        // Сделать проверку сайта по ID
        [HttpPost("{id:guid}/checks")]
        public async Task<IActionResult> RunCheck(
        Guid id,
        CancellationToken cancellationToken)
        {
            var result = await _checkService.RunAsync(
                id, cancellationToken);

            if (result is null)
            {
                return NotFound();
            }

            return Ok(new
            {
                result.Id,
                result.MonitorId,
                result.CheckedAtUtc,
                result.StatusCode,
                result.ResponseTimeMs,
                result.IsSuccessful,
                result.ErrorMessage
            });
        }

        // Получить список последних проверок
        [HttpGet("{id:guid}/checks")]
        public async Task<IActionResult> GetChecks(
        Guid id,
        CancellationToken cancellationToken)    
        {
             bool monitorExists = await _db.Monitors
                  .AnyAsync(site => site.Id == id, cancellationToken);

            if (!monitorExists)
            {
                return NotFound();
            }

            var checks = await _db.CheckResults
                .AsNoTracking()
                .Where(check => check.MonitorId == id)
                .OrderByDescending(check => check.CheckedAtUtc)
                .Take(50)
                .Select(check => new
                {
                    check.Id,
                    check.MonitorId,
                    check.CheckedAtUtc,
                    check.StatusCode,
                    check.ResponseTimeMs,
                    check.IsSuccessful,
                    check.ErrorMessage
                })
                .ToArrayAsync(cancellationToken);

            return Ok(checks);
        }

        // Получить статус сайта по ID
        [HttpGet("{id:guid}/status")]
        public async Task<IActionResult> GetStatus(
        Guid id,
        CancellationToken cancellationToken)
        {
            var monitor = await _db.Monitors
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    site => site.Id == id,
                    cancellationToken);

            if (monitor is null)
            {
                return NotFound();
            }

            var lastCheck = await _db.CheckResults
                .AsNoTracking()
                .Where(check => check.MonitorId == id)
                .OrderByDescending(check => check.CheckedAtUtc)
                .Select(check => new
                {
                    check.CheckedAtUtc,
                    check.StatusCode,
                    check.ResponseTimeMs,
                    check.IsSuccessful,
                    check.ErrorMessage
                })
                .FirstOrDefaultAsync(cancellationToken);

            return Ok(new
            {
                monitor.Id,
                monitor.Name,
                monitor.Url,
                monitor.CheckIntervalSeconds,
                LastCheck = lastCheck
            });
        }

        // Получить список последних инцидентов
        [HttpGet("{id:guid}/incidents")]
        public async Task<IActionResult> GetIncidents(
        Guid id,
        CancellationToken cancellationToken)
        {
            var monitorExists = await _db.Monitors
                .AnyAsync(site => site.Id == id, cancellationToken);

            if (!monitorExists)
            {
                return NotFound();
            }

            var incidents = await _db.Incidents
                .AsNoTracking()
                .Where(incident => incident.MonitorId == id)
                .OrderByDescending(incident => incident.StartedAtUtc)
                .Take(50)
                .Select(incident => new
                {
                    incident.Id,
                    incident.MonitorId,
                    incident.StartedAtUtc,
                    incident.ResolvedAtUtc,
                    incident.ErrorMessage,
                    IsOpen = incident.ResolvedAtUtc == null
                })
                .ToArrayAsync(cancellationToken);

            return Ok(incidents);
        }
    }
}