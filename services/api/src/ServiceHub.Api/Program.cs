using ServiceHub.Api.Extensions;
using ServiceHub.Api.Logging;

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

// Configure the middleware pipeline
app.UseServiceHubApi(app.Environment);

// Map endpoints
app.MapServiceHubEndpoints();

app.Run();

// Make Program class visible to tests
public partial class Program { }
