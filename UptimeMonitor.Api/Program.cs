using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using UptimeMonitor.Api.Configuration;
using UptimeMonitor.Api.Data;
using UptimeMonitor.Api.Security;
using UptimeMonitor.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddOptions<MonitoringOptions>()
    .BindConfiguration("Monitoring").ValidateDataAnnotations().ValidateOnStart();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Задайте ConnectionStrings__DefaultConnection или user-secrets. См. README.md.");
var apiKey = builder.Configuration["Security:ApiKey"];
if ((!builder.Environment.IsDevelopment() || !string.IsNullOrEmpty(apiKey))
    && (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 32 || apiKey.Length > 512))
    throw new InvalidOperationException("Security:ApiKey должен содержать от 32 до 512 символов.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0))));
builder.Services.AddHttpClient<WebsiteChecker>(client =>
{
    // A linked cancellation token in MonitorCheckService limits the entire redirect chain.
    client.Timeout = Timeout.InfiniteTimeSpan;
    client.DefaultRequestHeaders.UserAgent.ParseAdd("UptimeMonitor/1.0");
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    UseCookies = false,
    UseProxy = false,
    ConnectCallback = PublicUrlPolicy.ConnectAsync,
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    MaxResponseHeadersLength = 32
});
builder.Services.AddSingleton<MonitorLocks>();
builder.Services.AddSingleton<CheckConcurrency>();
builder.Services.AddScoped<MonitorCheckService>();
builder.Services.AddScoped<HistoryRetentionService>();
builder.Services.AddHostedService<MonitorWorker>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        !context.Request.Path.StartsWithSegments("/api")
            ? RateLimitPartition.GetNoLimiter("static")
            : RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("checks", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await Results.Problem(statusCode: 429, title: "Слишком много запросов", detail: "Повторите через минуту.")
            .ExecuteAsync(context.HttpContext);
    };
});

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    await next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();
app.MapGet("/api/session", () => Results.Ok(new { authenticated = true }));
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.Run();

public partial class Program { }
