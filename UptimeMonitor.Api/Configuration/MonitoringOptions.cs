using System.ComponentModel.DataAnnotations;

namespace UptimeMonitor.Api.Configuration;

public sealed class MonitoringOptions
{
    public bool Enabled { get; set; } = true;
    [Range(1, 60)] public int PollIntervalSeconds { get; set; } = 5;
    [Range(1, 60)] public int TimeoutSeconds { get; set; } = 10;
    [Range(1, 32)] public int MaxConcurrentChecks { get; set; } = 4;
    [Range(1, 3650)] public int RetentionDays { get; set; } = 30;
    [Range(1, 10000)] public int MaxMonitors { get; set; } = 1000;
}
