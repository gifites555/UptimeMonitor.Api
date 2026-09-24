using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UptimeMonitor.Api.Data;
using UptimeMonitor.Api.Models;

namespace UptimeMonitor.Api.Controllers
{
    [ApiController]
    [Route("api/monitors")]
    public class MonitorsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public MonitorsController(AppDbContext db)
        {
            _db = db;
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
    }
}