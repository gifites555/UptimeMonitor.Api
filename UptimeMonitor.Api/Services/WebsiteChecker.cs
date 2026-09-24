using System.Diagnostics;
using UptimeMonitor.Api.Models;

namespace UptimeMonitor.Api.Services
{
    public class WebsiteChecker
    {
        private readonly HttpClient _httpClient;

        public WebsiteChecker(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<CheckResult> CheckAsync(
            MonitoredSite monitor,
            CancellationToken cancellationToken = default)
        {
            var result = new CheckResult
            {
                Id = Guid.NewGuid(),
                MonitorId = monitor.Id,
                CheckedAtUtc = DateTime.UtcNow
            };

            if (!Uri.TryCreate(monitor.Url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp
                    && uri.Scheme != Uri.UriSchemeHttps))
            {
                result.ErrorMessage = "Адрес должен использовать HTTP или HTTPS.";
                return result;
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var response = await _httpClient.GetAsync(
                    uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                result.StatusCode = (int)response.StatusCode;
                result.IsSuccessful = response.IsSuccessStatusCode;
            }
            catch (HttpRequestException ex)
            {
                result.ErrorMessage = ex.Message;
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                result.ErrorMessage = "Сайт не ответил за отведённое время.";
            }
            finally
            {
                stopwatch.Stop();
                result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
            }

            return result;
        }
    }
}