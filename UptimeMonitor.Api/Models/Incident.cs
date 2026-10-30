namespace UptimeMonitor.Api.Models
{
    public class Incident
    {
        public Guid Id { get; set; }

        public Guid MonitorId { get; set; }

        public DateTime StartedAtUtc { get; set; }

        public DateTime? ResolvedAtUtc { get; set; }

        public string? ErrorMessage { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public MonitoredSite Monitor { get; set; } = null!;
    }
}
