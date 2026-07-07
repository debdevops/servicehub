using ServiceHub.Api.Extensions;
using ServiceHub.Api.Logging;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Simulator;
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

// When running in Simulator mode, register in-memory providers so the
// SimulatorController and related endpoints can resolve their dependencies.
// This must come AFTER AddServiceHubApi so simulator providers can replace
// real cloud provider registrations via services.Replace(...).
// Outside Simulator mode, register the live Azure provider instead — the
// CloudProviderRouter rejects duplicate provider types, so exactly one Azure
// ICloudMessagingProvider may be registered.
if (builder.Environment.IsEnvironment("Simulator"))
{
    builder.Services.AddSimulatorProviders();
}
else
{
    builder.Services.AddAzureProvider();
}

// Background workers (DLQ monitoring, message polling, anomaly detection).
// Registered after the provider block so DlqMonitorWorker scans through
// whichever ICloudMessagingProvider set is active for this host.
builder.Services.AddBackgroundWorkers();

var app = builder.Build();

// Wire Platform Event subscribers before any hosted service starts.
// This registers WebhookDlqSpikeHandler (and future handlers) with the
// InProcessPlatformEventBus singleton drain loop.
app.Services.SubscribePlatformEventHandlers();

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

        // Migration: Add CloudProvider to DlqMessages (multicloud attribution).
        // Existing rows predate multicloud DLQ monitoring, which was Azure-only, so
        // the column defaults to 'Azure' — matching CloudProviderType.Azure.ToString().
        if (!await ColumnExistsAsync(connection, "DlqMessages", "CloudProvider"))
        {
            logger.LogWarning(
                "DlqMessages table is missing the CloudProvider column — applying schema upgrade");
            await ExecuteNonQueryAsync(connection,
                "ALTER TABLE \"DlqMessages\" ADD COLUMN \"CloudProvider\" TEXT NOT NULL DEFAULT 'Azure'");
            logger.LogInformation("Schema upgrade applied: DlqMessages.CloudProvider added");
        }

        // Migration: Create AuditLogs table (added in v4.0.0 Persistent Audit Trail)
        // EnsureCreatedAsync creates this table in new databases; existing databases need the DDL.
        if (!await TableExistsAsync(connection, "AuditLogs"))
        {
            logger.LogWarning("AuditLogs table is missing — applying schema upgrade");
            await ExecuteNonQueryAsync(connection, """
                CREATE TABLE IF NOT EXISTS "AuditLogs" (
                    "Id"            TEXT NOT NULL CONSTRAINT "PK_AuditLogs" PRIMARY KEY,
                    "Timestamp"     TEXT NOT NULL,
                    "OwnerId"       TEXT NOT NULL,
                    "UserIdentity"  TEXT NOT NULL,
                    "Action"        TEXT NOT NULL,
                    "Outcome"       TEXT NOT NULL,
                    "NamespaceId"   TEXT,
                    "NamespaceName" TEXT,
                    "EntityName"    TEXT,
                    "CloudProvider" TEXT,
                    "Environment"   TEXT,
                    "ResourceName"  TEXT,
                    "SequenceNumber" INTEGER,
                    "DetailsJson"   TEXT,
                    "ErrorDetails"  TEXT,
                    "ClientIp"      TEXT,
                    "UserAgent"     TEXT,
                    "CorrelationId" TEXT,
                    "HttpMethod"    TEXT,
                    "HttpPath"      TEXT
                );
                CREATE INDEX IF NOT EXISTS "IX_AuditLogs_Timestamp"
                    ON "AuditLogs" ("Timestamp");
                CREATE INDEX IF NOT EXISTS "IX_AuditLogs_Owner_Timestamp"
                    ON "AuditLogs" ("OwnerId", "Timestamp");
                CREATE INDEX IF NOT EXISTS "IX_AuditLogs_Owner_Namespace_Timestamp"
                    ON "AuditLogs" ("OwnerId", "NamespaceId", "Timestamp");
                CREATE INDEX IF NOT EXISTS "IX_AuditLogs_Action"
                    ON "AuditLogs" ("Action");
                """);
            logger.LogInformation("Schema upgrade applied: AuditLogs table and indexes created");
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

static async Task<bool> TableExistsAsync(
    System.Data.Common.DbConnection connection, string tableName)
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
    var param = cmd.CreateParameter();
    param.ParameterName = "@name";
    param.Value = tableName;
    cmd.Parameters.Add(param);
    var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
    return count > 0;
}


static async Task ExecuteNonQueryAsync(System.Data.Common.DbConnection connection, string sql)
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = sql;
    await cmd.ExecuteNonQueryAsync();
}

// Make Program class visible to tests
public partial class Program { }
