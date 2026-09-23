using System.ComponentModel.DataAnnotations;

namespace UptimeMonitor.Api.Models
{
    public class CreateMonitorRequest
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Url]
        public string Url { get; set; } = string.Empty;

        [Range(10, 86400)]
        public int CheckIntervalSeconds { get; set; } = 60;
    }
}