using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using UptimeMonitor.Api.Models;

namespace UptimeMonitor.Api.Controllers
{
    [ApiController]
    [Route("api/monitors")]
    public class MonitorsController : ControllerBase
    {
        private static readonly ConcurrentDictionary<Guid, MonitoredSite>
            Sites = new();
        
        [HttpGet]
        public ActionResult<MonitoredSite[]> Get()
        {
            return Ok(Sites.Values.ToArray());
        }
        // Поиск по ID
        [HttpGet("{id:guid}")]
        public ActionResult<MonitoredSite> GetById(Guid id)
        {
            if (Sites.TryGetValue(id, out var site))
            {
                return Ok(site);
            }

            return NotFound();
        }
        // Поиск по имени
        [HttpGet]
        public ActionResult<MonitoredSite[]> Get(
        [FromQuery] string? name = null)
        {
            var sites = Sites.Values.ToArray();

            if (!string.IsNullOrWhiteSpace(name))
            {
                sites = sites
                    .Where(site => site.Name.Contains(
                        name.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            return Ok(sites);
        }

        // Основной метод для добавления сайта в колекцию и создания объекта 
        [HttpPost]
        public ActionResult<MonitoredSite> Post(
            [FromBody] CreateMonitorRequest request)
        {
            var site = new MonitoredSite
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Url = request.Url,
                CheckIntervalSeconds = request.CheckIntervalSeconds
            };

            Sites.TryAdd(site.Id, site);

            return CreatedAtAction(
                nameof(GetById),
                new { id = site.Id },
                site);
        }
    }
}