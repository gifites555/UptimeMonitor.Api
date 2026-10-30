using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using UptimeMonitor.Api.Data;
using UptimeMonitor.Api.Services;

namespace UptimeMonitor.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Key = "automated-tests-only-not-a-production-key";
    private readonly SqliteConnection _sqlite = new("Data Source=:memory:");
    private readonly string? _mysql;
    private bool _initialized;
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Probe { get; set; } =
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    public bool UsesMySql => _mysql is not null;

    public ApiFactory()
    {
        if (Environment.GetEnvironmentVariable("UPTIME_TEST_MYSQL") is { Length: > 0 } connection)
        {
            var builder = new MySqlConnectionStringBuilder(connection) { Database = "uptime_tests_" + Guid.NewGuid().ToString("N") };
            _mysql = builder.ConnectionString;
        }
        else _sqlite.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=localhost;Database=unused;User=test;");
        builder.UseSetting("Security:ApiKey", Key);
        builder.UseSetting("Monitoring:Enabled", "false");
        builder.UseSetting("Monitoring:TimeoutSeconds", "1");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
            {
                if (_mysql is null) options.UseSqlite(_sqlite);
                else options.UseMySql(_mysql, new MySqlServerVersion(new Version(8, 0, 0)));
            });
            services.AddTransient(_ => new WebsiteChecker(new HttpClient(new StubHandler((r, t) => Probe(r, t)))));
        });
    }

    public async Task<HttpClient> CreateReadyClientAsync(bool authenticated = true)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (!_initialized)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (_mysql is null) await db.Database.EnsureCreatedAsync();
            else await db.Database.MigrateAsync();
            _initialized = true;
        }
        if (authenticated) client.DefaultRequestHeaders.Add("X-Api-Key", Key);
        return client;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_initialized && _mysql is not null)
        {
            // Only the randomly generated, test-owned database may be dropped.
            var name = new MySqlConnectionStringBuilder(_mysql).Database;
            if (!name.StartsWith("uptime_tests_", StringComparison.Ordinal) || name.Length != 45)
                throw new InvalidOperationException("Invalid test database name.");
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }
        await base.DisposeAsync();
        await _sqlite.DisposeAsync();
    }
}
