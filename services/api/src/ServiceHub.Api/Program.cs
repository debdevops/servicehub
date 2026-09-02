using ServiceHub.Api.Configuration;
using ServiceHub.Api.Extensions;
using ServiceHub.Api.Logging;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Aws;
using ServiceHub.Infrastructure.Gcp;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Core.Interfaces;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

var builder = WebApplication.CreateBuilder(args);

// Load appsettings.Local.json (git-ignored) for local dev secrets. Test hosts set
// SkipLocalSettings so a dev machine's local overrides can't leak into assertions.
if (!builder.Configuration.GetValue("Configuration:SkipLocalSettings", false))
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
}

// Configure logging with redaction. RedactingLoggerProvider is the sole console
// sink — do not also add the stock AddConsole() provider, which writes unredacted
// log lines to the same destination and defeats the redaction guarantee.
builder.Logging.ClearProviders();
builder.Services.AddSingleton<ILoggerProvider, RedactingLoggerProvider>();

if (builder.Environment.IsDevelopment())
{
    builder.Logging.SetMinimumLevel(LogLevel.Debug);
}
else
{
    builder.Logging.SetMinimumLevel(LogLevel.Information);
}

// Configure forwarded headers for reverse proxy scenarios.
// Secure by default: headers are trusted only if explicitly configured via appsettings
// or environment variables (ForwardedHeaders:Enabled=true, ForwardedHeaders:KnownProxies, etc).
// Azure App Service is auto-detected and enabled if WEBSITE_AUTH_ENABLED=true.
builder.Services.AddSecureForwardedHeaders(builder.Configuration, builder.Environment);

// Configure request body size limit (prevent large payload attacks)
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 5 * 1024 * 1024; // 5 MB

    // Suppress "Server: Kestrel" at the source. SecurityHeadersMiddleware also calls
    // Headers.Remove("Server"), but that runs in an OnStarting callback while Kestrel writes
    // its default Server header later, when the response is actually serialized — so the
    // removal never took effect and every response still advertised the server product.
    // This flag is the only thing that actually suppresses it.
    options.AddServerHeader = false;
});

// Add Application Insights telemetry (cost-effective configuration)
builder.Services.AddApplicationInsightsTelemetryConfiguration(builder.Configuration, builder.Environment);

// Add vendor-neutral OpenTelemetry (traces + metrics). Inert unless explicitly enabled or an
// OTLP endpoint is configured — see ObservabilityExtensions. Coexists with App Insights.
builder.Services.AddOpenTelemetryObservability(builder.Configuration);

// Add ServiceHub API services
builder.Services.AddServiceHubApi(builder.Configuration);

// Register the live Azure provider — the CloudProviderRouter rejects duplicate
// provider types, so exactly one Azure ICloudMessagingProvider may be registered.
builder.Services.AddAzureProvider();

// AWS/GCP are preview providers, disabled by default. Enabling a flag registers
// that provider's ICloudMessagingProvider, its client factory, and its
// connectivity health check ("aws-connectivity" / "gcp-connectivity").
// Registration is inert until a namespace for that provider exists.
if (builder.Configuration.GetValue("CloudProviders:Aws:Enabled", false))
{
    builder.Services.AddAwsProvider();
}

if (builder.Configuration.GetValue("CloudProviders:Gcp:Enabled", false))
{
    builder.Services.AddGcpProvider();
}

// Background workers (DLQ monitoring, message polling, anomaly detection).
// Registered after the provider block so DlqMonitorWorker scans through
// whichever ICloudMessagingProvider set is active for this host.
builder.Services.AddBackgroundWorkers();

var app = builder.Build();

// Enforce the single-instance invariant the recovery evidence ledger's hash chain depends on
// (roadmap W1.4). Resolved first, before anything else touches the data directory: a second
// instance already running against the same directory fails fast here with a clear message
// instead of silently corrupting the ledger's hash chain later.
app.Services.GetRequiredService<ServiceHub.Infrastructure.Persistence.SqliteInstanceLock>();

// Emit a single, secret-free summary of the effective configuration for operability.
app.LogStartupSummary();

// Validate production configuration — fail fast if required settings are missing or invalid
ProductionConfigurationValidator.ValidateProduction(
    app.Configuration,
    app.Environment,
    app.Logger);

// Eagerly resolve the connection-string protector so a broken or invalid encryption key
// registry (Security:EncryptionKeyRegistry / Security:EncryptionKey) fails startup with a clear
// error instead of surfacing lazily on the first namespace request — in every environment, not
// just Production (ProductionConfigurationValidator above only runs there).
app.Services.GetRequiredService<IConnectionStringProtector>();

// Wire Platform Event subscribers before any hosted service starts.
// This registers WebhookDlqSpikeHandler (and future handlers) with the
// InProcessPlatformEventBus singleton drain loop.
app.Services.SubscribePlatformEventHandlers();

// Fan out platform events to connected SSE clients (GET /api/v1/events/stream).
// In-process bus: clients only see events published by THIS instance — acceptable
// while ServiceHub is single-instance (SQLite already pins deployment to one host).
app.Services.GetRequiredService<ServiceHub.Core.Interfaces.IPlatformEventBus>()
    .Subscribe(app.Services.GetRequiredService<ServiceHub.Api.Services.PlatformEventStreamBroker>().HandleAsync);

// Forwarded headers must be first in pipeline (before any middleware that reads client IP)
app.UseForwardedHeaders();

// Ensure DLQ Intelligence database schema is up to date before serving requests
using (var scope = app.Services.CreateScope())
{
    try
    {
        var dlqDbContext = scope.ServiceProvider.GetRequiredService<DlqDbContext>();

        // Databases created before EF Core Migrations were introduced (via EnsureCreatedAsync)
        // already have every table the InitialCreate migration would create, but no
        // __EFMigrationsHistory row recording that. Without this, MigrateAsync() would treat
        // the database as empty and fail trying to re-create existing tables. Record
        // InitialCreate as already applied first — idempotent, no-ops on fresh or
        // already-migrated databases.
        await BootstrapMigrationsHistoryForExistingDatabaseAsync(dlqDbContext, app.Logger);

        await dlqDbContext.Database.MigrateAsync();
        app.Logger.LogInformation("DLQ Intelligence database schema is up to date");

        // Reconcile messages stranded mid-replay or mid-purge by a previous process. This must
        // run here — after the schema is ready but before any hosted service starts — so that
        // every claimed row it sees is provably abandoned rather than actively in flight. See
        // InterruptedOperationRecovery for the full rationale.
        try
        {
            var recoveryLedger = scope.ServiceProvider.GetRequiredService<IRecoveryLedger>();
            await InterruptedOperationRecovery.ReconcileInterruptedOperationsAsync(
                dlqDbContext, recoveryLedger, app.Logger);
        }
        catch (Exception recoveryEx)
        {
            // A failed reconciliation must not stop the app: the stranded rows stay stranded,
            // which is the pre-existing behaviour, and the operator gets a logged reason.
            app.Logger.LogError(
                recoveryEx, "Failed to reconcile DLQ messages stranded mid-operation at startup");
        }
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

// Records the InitialCreate migration as already applied when a pre-Migrations database
// (created via the old EnsureCreatedAsync path) is detected — i.e. its tables already exist
// but it has no __EFMigrationsHistory table yet. No-ops on a fresh database (no tables to
// find) and on an already-migrated one (history table already exists).
static async Task BootstrapMigrationsHistoryForExistingDatabaseAsync(DlqDbContext dbContext, ILogger logger)
{
    var historyRepository = dbContext.GetService<IHistoryRepository>();
    if (await historyRepository.ExistsAsync())
        return;

    var connection = dbContext.Database.GetDbConnection();
    var wasOpen = connection.State == System.Data.ConnectionState.Open;
    if (!wasOpen)
        await connection.OpenAsync();

    try
    {
        var appTablesExist = await TableExistsAsync(connection, "DlqMessages");
        if (!appTablesExist)
            return;

        logger.LogWarning(
            "Existing database predates EF Core Migrations — recording InitialCreate as already applied");

        await ExecuteNonQueryAsync(connection, historyRepository.GetCreateIfNotExistsScript());

        // Always bootstrap against InitialCreate specifically, not "whichever migration
        // exists" — once later migrations are added, GetMigrations() returns more than one
        // and a positional Single() would throw for every pre-Migrations database out there.
        var migrationId = dbContext.Database.GetMigrations()
            .Single(id => id.EndsWith("_InitialCreate", StringComparison.Ordinal));
        var productVersion = ProductInfo.GetVersion();
        await ExecuteNonQueryAsync(connection, historyRepository.GetInsertScript(new HistoryRow(migrationId, productVersion)));

        logger.LogInformation("Schema upgrade applied: InitialCreate recorded for pre-existing database");
    }
    finally
    {
        if (!wasOpen)
            connection.Close();
    }
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
