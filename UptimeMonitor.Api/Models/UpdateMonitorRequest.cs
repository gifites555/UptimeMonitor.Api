using System.ComponentModel.DataAnnotations;

namespace UptimeMonitor.Api.Models
{
    public class UpdateMonitorRequest
    {
        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [PublicHttpUrl]
        [StringLength(2048)]
        public string Url { get; set; } = string.Empty;

        [Range(10, 86400)]
        public int CheckIntervalSeconds { get; set; } = 60;
    }
}
