using System.Net;
using UptimeMonitor.Api.Models;
using UptimeMonitor.Api.Services;

namespace UptimeMonitor.Api.Tests;

public class WebsiteCheckerTests
{
    private static readonly MonitoredSite Monitor = new() { Id = Guid.NewGuid(), Url = "https://example.com" };

    [Theory]
    [InlineData(200, true)]
    [InlineData(204, true)]
    [InlineData(403, false)]
    [InlineData(429, false)]
    [InlineData(503, false)]
    public async Task ClassifiesHttpResponses(int status, bool successful)
    {
        using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status))));
        var result = await new WebsiteChecker(client).CheckAsync(Monitor);
        Assert.Equal(successful, result.IsSuccessful);
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(Monitor.Id, result.MonitorId);
        Assert.Equal(DateTimeKind.Utc, result.CheckedAtUtc.Kind);
        Assert.Equal(successful, result.ErrorMessage is null);
    }

    [Fact]
    public async Task FollowsRelativeRedirects()
    {
        var calls = 0;
        using var client = new HttpClient(new StubHandler((request, _) =>
        {
            calls++;
            if (calls == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri("/healthy", UriKind.Relative);
                return Task.FromResult(response);
            }
            Assert.Equal("/healthy", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));
        Assert.True((await new WebsiteChecker(client).CheckAsync(Monitor)).IsSuccessful);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://169.254.169.254")]
    [InlineData("file:///etc/passwd")]
    public async Task DoesNotFollowUnsafeRedirects(string destination)
    {
        var calls = 0;
        using var client = new HttpClient(new StubHandler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri(destination);
            return Task.FromResult(response);
        }));
        Assert.False((await new WebsiteChecker(client).CheckAsync(Monitor)).IsSuccessful);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task StopsRedirectLoops()
    {
        var calls = 0;
        using var client = new HttpClient(new StubHandler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://example.com");
            return Task.FromResult(response);
        }));
        Assert.False((await new WebsiteChecker(client).CheckAsync(Monitor)).IsSuccessful);
        Assert.Equal(6, calls);
    }

    [Fact]
    public async Task CancellationIsNotRecordedAsDowntime()
    {
        using var client = new HttpClient(new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WebsiteChecker(client).CheckAsync(Monitor, cancellation.Token));
    }

    [Fact]
    public async Task HttpTimeoutBecomesFailure()
    {
        using var client = new HttpClient(new StubHandler((_, _) => throw new TaskCanceledException()));
        var result = await new WebsiteChecker(client).CheckAsync(Monitor);
        Assert.False(result.IsSuccessful);
        Assert.Contains("время", result.ErrorMessage);
    }

    [Fact]
    public async Task NetworkErrorsDoNotExposeConnectionDetails()
    {
        using var client = new HttpClient(new StubHandler((_, _) => throw new HttpRequestException("internal host 10.0.0.1")));
        var result = await new WebsiteChecker(client).CheckAsync(Monitor);
        Assert.False(result.IsSuccessful);
        Assert.DoesNotContain("10.0.0.1", result.ErrorMessage);
    }
}

internal sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken);
}
