namespace UptimeMonitor.Api.Models
{
    public class CheckResult
    {
        public Guid Id { get; set; }

        public Guid MonitorId { get; set; }

        public DateTime CheckedAtUtc { get; set; }

        public int? StatusCode { get; set; }

        public long ResponseTimeMs { get; set; }

        public bool IsSuccessful { get; set; }

        public string? ErrorMessage { get; set; }

        public MonitoredSite Monitor { get; set; } = null!;
    }
}