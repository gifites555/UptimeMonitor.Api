using Microsoft.EntityFrameworkCore;
using UptimeMonitor.Api.Data;
using UptimeMonitor.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Строка подключения DefaultConnection не найдена.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        connectionString,
        ServerVersion.AutoDetect(connectionString)));

builder.Services.AddHttpClient<WebsiteChecker>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHostedService<MonitorWorker>();
builder.Services.AddScoped<MonitorCheckService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();

app.Run();