using ServiceHub.Api.Extensions;
using ServiceHub.Api.Logging;
using ServiceHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Load appsettings.Local.json (git-ignored) for local dev secrets
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

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

// Configure forwarded headers for reverse proxy (Azure App Service)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Configure request body size limit (prevent large payload attacks)
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 5 * 1024 * 1024; // 5 MB
});

// Add Application Insights telemetry (cost-effective configuration)
builder.Services.AddApplicationInsightsTelemetryConfiguration(builder.Configuration, builder.Environment);

// Add ServiceHub API services
builder.Services.AddServiceHubApi(builder.Configuration);

var app = builder.Build();

// Forwarded headers must be first in pipeline (before any middleware that reads client IP)
app.UseForwardedHeaders();

// Ensure DLQ Intelligence database schema exists before serving requests
using (var scope = app.Services.CreateScope())
{
    try
    {
        var dlqDbContext = scope.ServiceProvider.GetRequiredService<DlqDbContext>();
        await dlqDbContext.Database.EnsureCreatedAsync();

        // Apply incremental schema migrations for databases created before OwnerId support.
        // EnsureCreatedAsync() creates new databases correctly but does NOT alter existing ones.
        // This runs every startup and is idempotent — safe to run on already-migrated databases.
        await ApplySchemaUpgradesAsync(dlqDbContext, app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to initialize DLQ Intelligence database schema");
        // In production, fail fast if database can't initialize
        if (!app.Environment.IsDevelopment())
        {
            throw;
        }
    }
}

// Configure the middleware pipeline
app.UseServiceHubApi(app.Environment);

// Map endpoints
app.MapServiceHubEndpoints();

app.Run();

// Applies incremental SQLite schema upgrades that EnsureCreatedAsync() cannot handle.
// Checks for missing columns and adds them with safe defaults. Idempotent.
static async Task ApplySchemaUpgradesAsync(DlqDbContext dbContext, ILogger logger)
{
    var connection = dbContext.Database.GetDbConnection();
    var wasOpen = connection.State == System.Data.ConnectionState.Open;
    if (!wasOpen)
        await connection.OpenAsync();

    try
    {
        // Migration: Add OwnerId to DlqMessages (added in v3.1.0 multi-tenant support)
        if (!await ColumnExistsAsync(connection, "DlqMessages", "OwnerId"))
        {
            logger.LogWarning(
                "DlqMessages table is missing the OwnerId column — applying schema upgrade");
            await ExecuteNonQueryAsync(connection,
                "ALTER TABLE \"DlqMessages\" ADD COLUMN \"OwnerId\" TEXT NOT NULL DEFAULT '__spa__'");
            logger.LogInformation("Schema upgrade applied: DlqMessages.OwnerId added");
        }

        // Migration: Add OwnerId to AutoReplayRules (added in v3.1.0 multi-tenant support)
        if (!await ColumnExistsAsync(connection, "AutoReplayRules", "OwnerId"))
        {
            logger.LogWarning(
                "AutoReplayRules table is missing the OwnerId column — applying schema upgrade");
            await ExecuteNonQueryAsync(connection,
                "ALTER TABLE \"AutoReplayRules\" ADD COLUMN \"OwnerId\" TEXT NOT NULL DEFAULT '__spa__'");
            logger.LogInformation("Schema upgrade applied: AutoReplayRules.OwnerId added");
        }
    }
    finally
    {
        if (!wasOpen)
            connection.Close();
    }
}

static async Task<bool> ColumnExistsAsync(
    System.Data.Common.DbConnection connection, string tableName, string columnName)
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = $"PRAGMA table_info(\"{tableName}\")";
    using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        // Column 1 is the column name in PRAGMA table_info output
        if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            return true;
    }
    return false;
}

static async Task ExecuteNonQueryAsync(System.Data.Common.DbConnection connection, string sql)
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = sql;
    await cmd.ExecuteNonQueryAsync();
}

// Make Program class visible to tests
public partial class Program { }
