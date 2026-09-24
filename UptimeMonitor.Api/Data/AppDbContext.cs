using Microsoft.EntityFrameworkCore;
using UptimeMonitor.Api.Models;

namespace UptimeMonitor.Api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<MonitoredSite> Monitors => Set<MonitoredSite>();

        public DbSet<CheckResult> CheckResults => Set<CheckResult>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            var monitor = modelBuilder.Entity<MonitoredSite>();

            var check = modelBuilder.Entity<CheckResult>();

            check.ToTable("check_results");

            check.HasKey(result => result.Id);

            check.Property(result => result.Id)
                .HasColumnType("char(36)")
                .ValueGeneratedNever();

            check.Property(result => result.MonitorId)
                .HasColumnType("char(36)");

            check.Property(result => result.CheckedAtUtc)
                .HasColumnType("datetime(6)")
                .IsRequired();

            check.Property(result => result.ErrorMessage)
                .HasColumnType("text");

            check.HasOne(result => result.Monitor)
                .WithMany()
                .HasForeignKey(result => result.MonitorId)
                .OnDelete(DeleteBehavior.Cascade);

            check.HasIndex(result => new
            {
                result.MonitorId,
                result.CheckedAtUtc
            });

            monitor.ToTable("monitors");

            monitor.HasKey(site => site.Id);

            monitor.Property(site => site.Id)
                .HasColumnType("char(36)")
                .ValueGeneratedNever();

            monitor.Property(site => site.Name)
                .HasMaxLength(200)
                .IsRequired();

            monitor.Property(site => site.Url)
                .HasMaxLength(2048)
                .IsRequired();

            monitor.Property(site => site.CheckIntervalSeconds)
                .HasDefaultValue(60)
                .IsRequired();
        }
    }
}