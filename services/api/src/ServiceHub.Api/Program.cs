using ServiceHub.Api.Extensions;
using ServiceHub.Api.Logging;
using ServiceHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configure logging with redaction
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddSingleton<ILoggerProvider, RedactingLoggerProvider>();

if (builder.Environment.IsDevelopment())
{
    builder.Logging.SetMinimumLevel(LogLevel.Debug);
}
else
{
    builder.Logging.SetMinimumLevel(LogLevel.Information);
}

// Add ServiceHub API services
builder.Services.AddServiceHubApi(builder.Configuration);

var app = builder.Build();

// Ensure DLQ Intelligence database schema exists before serving requests
using (var scope = app.Services.CreateScope())
{
    try
    {
        var dlqDbContext = scope.ServiceProvider.GetRequiredService<DlqDbContext>();
        await dlqDbContext.Database.EnsureCreatedAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to initialize DLQ Intelligence database schema");
    }
}

// Configure the middleware pipeline
app.UseServiceHubApi(app.Environment);

// Map endpoints
app.MapServiceHubEndpoints();

app.Run();

// Make Program class visible to tests
public partial class Program { }
