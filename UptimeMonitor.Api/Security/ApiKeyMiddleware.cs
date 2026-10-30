using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace UptimeMonitor.Api.Security;

public sealed class ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration, IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api")) { await next(context); return; }
        context.Response.Headers.CacheControl = "no-store";
        var configuredKey = configuration["Security:ApiKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            if (environment.IsDevelopment() && context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address))
            { await next(context); return; }
        }
        else
        {
            var supplied = context.Request.Headers["X-Api-Key"].ToString();
            if (supplied.Length <= 512 && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey)),
                SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
            { await next(context); return; }
        }
        await Results.Problem(statusCode: 401, title: "Требуется ключ доступа", detail: "Передайте ключ в заголовке X-Api-Key.").ExecuteAsync(context);
    }
}
