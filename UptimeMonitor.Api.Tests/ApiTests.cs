using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UptimeMonitor.Api.Data;
using UptimeMonitor.Api.Models;
using UptimeMonitor.Api.Services;

namespace UptimeMonitor.Api.Tests;

public class ApiTests
{
    private static object Request(string url = "https://example.com", string name = "Example") =>
        new { name, url, checkIntervalSeconds = 60 };

    private static async Task<Guid> CreateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/monitors", Request());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var monitor = await response.Content.ReadFromJsonAsync<MonitoredSite>();
        Assert.NotNull(response.Headers.Location);
        return monitor!.Id;
    }

    [Fact]
    public async Task ApiRequiresKeyButStaticPageAndLivenessArePublic()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.CreateReadyClientAsync(authenticated: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/monitors")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        var page = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Сайты под наблюдением", await page.Content.ReadAsStringAsync());
        Assert.True(page.Headers.Contains("Content-Security-Policy"));
    }

    [Theory]
    [InlineData("ftp://example.com", "Example", 60)]
    [InlineData("http://127.0.0.1", "Example", 60)]
    [InlineData("https://example.com", "   ", 60)]
    [InlineData("https://example.com", "Example", 0)]
    [InlineData("https://example.com", "Example", 86401)]
    public async Task RejectsInvalidMonitor(string url, string name, int interval)
    {
        await using var factory = new ApiFactory();
        using var client = await factory.CreateReadyClientAsync();
        var response = await client.PostAsJsonAsync("/api/monitors", new { name, url, checkIntervalSeconds = interval });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatesEditsSearchesAndDeletesMonitor()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.CreateReadyClientAsync();
        var id = await CreateAsync(client);
        var status = await client.GetFromJsonAsync<JsonElement>($"/api/monitors/{id}/status");
        Assert.Equal("unknown", status.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/monitors/{id}", Request(name: "Renamed"))).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<MonitoredSite[]>("/api/monitors?name=Renamed"))!);
        Assert.Empty((await client.GetFromJsonAsync<MonitoredSite[]>("/api/monitors?name=Absent"))!);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/monitors/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/monitors/{id}")).StatusCode);
    }

    [Fact]
    public async Task ConsecutiveFailuresOpenOneIncidentAndRecoveryClosesIt()
    {
        await using var factory = new ApiFactory();
        factory.Probe = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = await factory.CreateReadyClientAsync();
        var id = await CreateAsync(client);
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/monitors/{id}/checks", null)).StatusCode);
        var incidents = await client.GetFromJsonAsync<JsonElement[]>($"/api/monitors/{id}/incidents");
        Assert.Single(incidents!);
        Assert.True(incidents![0].GetProperty("isOpen").GetBoolean());

        factory.Probe = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        await client.PostAsync($"/api/monitors/{id}/checks", null);
        incidents = await client.GetFromJsonAsync<JsonElement[]>($"/api/monitors/{id}/incidents");
        Assert.Single(incidents!);
        Assert.False(incidents![0].GetProperty("isOpen").GetBoolean());
        Assert.EndsWith("Z", incidents[0].GetProperty("resolvedAtUtc").GetString());

        var overview = await client.GetFromJsonAsync<JsonElement>("/api/monitors/overview");
        var row = overview.GetProperty("monitors")[0];
        Assert.Equal("up", row.GetProperty("status").GetString());
        Assert.Equal(3, row.GetProperty("checksLast24Hours").GetInt32());
        Assert.Equal(33.33, row.GetProperty("uptimePercent24Hours").GetDouble());
        Assert.False(row.GetProperty("lastCheck").TryGetProperty("monitor", out _));

        factory.Probe = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
        await client.PostAsync($"/api/monitors/{id}/checks", null);
        incidents = await client.GetFromJsonAsync<JsonElement[]>($"/api/monitors/{id}/incidents");
        Assert.Equal(2, incidents!.Length);
        Assert.Single(incidents, i => i.GetProperty("isOpen").GetBoolean());
    }

    [Fact]
    public async Task DeleteCascadesAndUrlChangesCannotMixHistory()
    {
        await using var factory = new ApiFactory();
        factory.Probe = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
        using var client = await factory.CreateReadyClientAsync();
        var id = await CreateAsync(client);
        await client.PostAsync($"/api/monitors/{id}/checks", null);
        var update = await client.PutAsJsonAsync($"/api/monitors/{id}", Request("https://other.example.com"));
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/monitors/{id}")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.CheckResults.ToArrayAsync());
        Assert.Empty(await db.Incidents.ToArrayAsync());
    }

    [Fact]
    public async Task OldResultIsStaleAndExcludedFromDailyStatistics()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.CreateReadyClientAsync();
        var id = await CreateAsync(client);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CheckResults.Add(new CheckResult { Id = Guid.NewGuid(), MonitorId = id, CheckedAtUtc = DateTime.UtcNow.AddDays(-2), IsSuccessful = true });
            await db.SaveChangesAsync();
        }
        var overview = await client.GetFromJsonAsync<JsonElement>("/api/monitors/overview");
        var row = overview.GetProperty("monitors")[0];
        Assert.Equal("stale", row.GetProperty("status").GetString());
        Assert.Equal(0, row.GetProperty("checksLast24Hours").GetInt32());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("uptimePercent24Hours").ValueKind);
    }

    [Fact]
    public async Task WorkerDoesNotRepeatAFreshManualCheck()
    {
        await using var factory = new ApiFactory();
        var calls = 0;
        factory.Probe = (_, _) => { Interlocked.Increment(ref calls); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); };
        using var client = await factory.CreateReadyClientAsync();
        var id = await CreateAsync(client);
        await client.PostAsync($"/api/monitors/{id}/checks", null);
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<MonitorCheckService>().RunAsync(id, default, onlyIfDue: true);
        Assert.Null(result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ConcurrentDueChecksAreCoalesced()
    {
        await using var factory = new ApiFactory();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Probe = async (_, token) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        using var client = await factory.CreateReadyClientAsync();
        var id = await CreateAsync(client);
        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<MonitorCheckService>().RunAsync(id, default, onlyIfDue: true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = secondScope.ServiceProvider.GetRequiredService<MonitorCheckService>().RunAsync(id, default, onlyIfDue: true);
        release.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.Single(results, r => r is not null);
    }

    [Fact]
    public async Task MissingMonitorsAndInvalidPaginationHaveCorrectStatusCodes()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.CreateReadyClientAsync();
        var id = Guid.NewGuid();
        foreach (var suffix in new[] { "", "/status", "/checks", "/incidents" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/monitors/{id}{suffix}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/monitors/{id}/checks", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/monitors?limit=501")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/monitors/{id}/checks?offset=-1")).StatusCode);
    }

    [Fact]
    public async Task ExcessManualChecksAreRateLimited()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.CreateReadyClientAsync();
        var id = await CreateAsync(client);
        for (var i = 0; i < 20; i++) Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/monitors/{id}/checks", null)).StatusCode);
        var response = await client.PostAsync($"/api/monitors/{id}/checks", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task MySqlModelMatchesExistingMigrations()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql("Server=localhost;Database=model_only;User=test", new MySqlServerVersion(new Version(8, 0, 0))).Options;
        await using var db = new AppDbContext(options);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task OverallTimeoutCreatesFailureAndIncident()
    {
        await using var factory = new ApiFactory();
        factory.Probe = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        using var client = await factory.CreateReadyClientAsync();
        var id = await CreateAsync(client);
        var response = await client.PostAsync($"/api/monitors/{id}/checks", null);
        var result = await response.Content.ReadFromJsonAsync<CheckResult>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(result!.IsSuccessful);
        Assert.Contains("время", result.ErrorMessage);
        Assert.Single((await client.GetFromJsonAsync<Incident[]>($"/api/monitors/{id}/incidents"))!);
    }

    [Fact]
    public async Task CancelledCheckDoesNotPersistResultOrIncident()
    {
        await using var factory = new ApiFactory();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Probe = async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        using var client = await factory.CreateReadyClientAsync();
        var id = await CreateAsync(client);
        await using var scope = factory.Services.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();
        var check = scope.ServiceProvider.GetRequiredService<MonitorCheckService>().RunAsync(id, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.CheckResults.ToArrayAsync());
        Assert.Empty(await db.Incidents.ToArrayAsync());
    }

    [Fact]
    public async Task OpenIncidentPreventsUrlChangeAfterOldChecksExpire()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.CreateReadyClientAsync();
        var id = await CreateAsync(client);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Incidents.Add(new Incident { Id = Guid.NewGuid(), MonitorId = id, StartedAtUtc = DateTime.UtcNow.AddDays(-40) });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PutAsJsonAsync($"/api/monitors/{id}", Request("https://other.example.com"))).StatusCode);
    }

    [Fact]
    public async Task RetentionRemovesOldHistoryButPreservesOpenIncidents()
    {
        await using var factory = new ApiFactory();
        using var client = await factory.CreateReadyClientAsync();
        var id = await CreateAsync(client);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        db.CheckResults.AddRange(
            new CheckResult { Id = Guid.NewGuid(), MonitorId = id, CheckedAtUtc = now.AddDays(-40) },
            new CheckResult { Id = Guid.NewGuid(), MonitorId = id, CheckedAtUtc = now });
        db.Incidents.AddRange(
            new Incident { Id = Guid.NewGuid(), MonitorId = id, StartedAtUtc = now.AddDays(-45), ResolvedAtUtc = now.AddDays(-40) },
            new Incident { Id = Guid.NewGuid(), MonitorId = id, StartedAtUtc = now.AddDays(-45) },
            new Incident { Id = Guid.NewGuid(), MonitorId = id, StartedAtUtc = now.AddDays(-40), ResolvedAtUtc = now });
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<HistoryRetentionService>().CleanupAsync(default);
        Assert.Single(await db.CheckResults.AsNoTracking().ToArrayAsync());
        var remaining = await db.Incidents.AsNoTracking().ToArrayAsync();
        Assert.Equal(2, remaining.Length);
        Assert.Single(remaining, i => i.ResolvedAtUtc is null);
    }
}
