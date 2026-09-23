namespace UptimeMonitor.Api.Models
{
    public class MonitoredSite
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;

        public int CheckIntervalSeconds { get; set; } = 60;
    }
}