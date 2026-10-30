using System.Diagnostics;
using System.Net;
using UptimeMonitor.Api.Models;

namespace UptimeMonitor.Api.Services;

public sealed class WebsiteChecker(HttpClient httpClient)
{
    public async Task<CheckResult> CheckAsync(MonitoredSite monitor, CancellationToken cancellationToken = default)
    {
        var result = new CheckResult
        {
            Id = Guid.NewGuid(), MonitorId = monitor.Id, CheckedAtUtc = DateTime.UtcNow
        };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!PublicUrlPolicy.TryValidate(monitor.Url, out var uri))
                throw new HttpRequestException("Адрес не разрешён для публичного HTTP/HTTPS мониторинга.");

            // Every redirect destination must pass the same URL and connection policy.
            for (var redirects = 0; ; redirects++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
                    or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    if (redirects >= 5) throw new HttpRequestException("Слишком много перенаправлений.");
                    if (response.Headers.Location is not { } location
                        || !Uri.TryCreate(uri, location, out var next)
                        || !PublicUrlPolicy.TryValidate(next.AbsoluteUri, out var validated))
                        throw new HttpRequestException("Сайт перенаправляет на недопустимый адрес.");
                    uri = validated;
                    continue;
                }
                result.StatusCode = (int)response.StatusCode;
                result.IsSuccessful = response.IsSuccessStatusCode;
                if (!result.IsSuccessful) result.ErrorMessage = $"Сайт вернул HTTP {result.StatusCode}.";
                break;
            }
        }
        catch (HttpRequestException)
        {
            // Do not expose internal addresses or TLS/DNS diagnostics to clients.
            result.ErrorMessage = "Ошибка сети, TLS или запрещённый адрес/перенаправление.";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result.ErrorMessage = "Сайт не ответил за отведённое время.";
        }
        finally { result.ResponseTimeMs = stopwatch.ElapsedMilliseconds; }
        return result;
    }
}
