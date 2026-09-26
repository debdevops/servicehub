using ServiceHub.Infrastructure.BulkOperations;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Extensions;
using ServiceHub.Api.Middleware;
using ServiceHub.Infrastructure.Agents;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Routing;
using ServiceHub.Providers.Aws;
using ServiceHub.Providers.Azure;
using ServiceHub.Providers.Gcp;

var builder = WebApplication.CreateBuilder(args);

// ── Services ────────────────────────────────────────────────────────────────────────────────────
// The composition root, and the ONLY place ServiceHub.Infrastructure and the ServiceHub.Providers.*
// projects meet (ADR-0014 D5).

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        // Enums travel as words ("azure", "dev"), never as numbers a client has to decode.
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));
builder.Services.AddProblemDetails(options =>
{
    // Every failure leaves with a stable machine-readable code and a human sentence. A bare status
    // code with no body is a bug (ARCHITECTURE §5.2).
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions.TryAdd("code", ProblemCodes.ForStatus(context.ProblemDetails.Status));
        context.ProblemDetails.Extensions.TryAdd("correlationId", context.HttpContext.TraceIdentifier);
    };
});

builder.Services.AddHealthChecks();
builder.Services.Configure<ServiceHub.Core.Models.OidcOptions>(builder.Configuration.GetSection(ServiceHub.Core.Models.OidcOptions.SectionName));
builder.Services.AddServiceHubPersistence();
builder.Services.AddCloudProviderRouting();

// One line per cloud: this is the only place the API knows a provider exists.
builder.Services.AddAzureProvider();
builder.Services.AddAwsProvider();
builder.Services.AddGcpProvider();
builder.Services.AddOpenApi();

// The agent platform. Agents arrive one file and one registration line at a time (unit 2.1 onwards).
builder.Services.AddAgentPlatform();
ServiceHub.Infrastructure.Webhooks.WebhookServiceCollectionExtensions.AddWebhooks(builder.Services, builder.Configuration);
builder.Services.AddAgent<DlqMonitorAgent>();
builder.Services.AddAgent<RecoveryVerificationAgent>();
builder.Services.AddAgent<BulkOperationAgent>();
builder.Services.AddAgent<ServiceHub.Infrastructure.Rules.AutoReplayAgent>();
builder.Services.AddAgent<AutonomyEvaluationAgent>();
builder.Services.AddAgent<ServiceHub.Infrastructure.Insights.AnomalyInsightAgent>();
builder.Services.AddAgent<ServiceHub.Infrastructure.Insights.BacklogInsightAgent>();
builder.Services.AddAgent<ServiceHub.Infrastructure.Insights.CorrelationInsightAgent>();
builder.Services.AddAgent<ServiceHub.Infrastructure.Insights.NarrationInsightAgent>();
if (builder.Configuration.GetValue<int>("Backup:ScheduledBackupIntervalHours") > 0)
{
    builder.Services.AddAgent<BackupAgent>(); // off by default (unit 6.13)
}

builder.Services.AddSingleton<ServiceHub.Api.Services.PlatformEventStreamBroker>();

var app = builder.Build();

// The stream broker listens to the bus for the life of the process, so a browser tab can be told when something changed.
app.Services.GetRequiredService<ServiceHub.Core.Interfaces.IPlatformEventBus>()
    .Subscribe(app.Services.GetRequiredService<ServiceHub.Api.Services.PlatformEventStreamBroker>().HandleAsync);
// Escalations also go to Slack / Teams / a webhook when one is configured (unit 5.5) — best-effort, never the source of truth.
app.Services.GetRequiredService<ServiceHub.Core.Interfaces.IPlatformEventBus>()
    .Subscribe(app.Services.GetRequiredService<ServiceHub.Infrastructure.Webhooks.WebhookEscalationHandler>().HandleAsync);

// Take the single-instance lock and apply migrations BEFORE serving. A second instance against the
// same data directory, or a database this version does not recognise, stops here with a clear
// message rather than starting up and corrupting anything (ADR-0003, ADR-0015 D4).
await app.Services.InitializeServiceHubDatabaseAsync().ConfigureAwait(false);

// ── Pipeline ────────────────────────────────────────────────────────────────────────────────────
// Order matters and is deliberate: correlate, then harden, then handle errors, then route.

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    // No stack trace ever reaches a response, in any environment.
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ServiceHub.Api");
    logger.LogError(feature?.Error, "Unhandled exception while handling {Path}", context.Request.Path);

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await Results.Problem(
        title: "Something went wrong",
        detail: "ServiceHub could not complete this request. The failure has been logged.",
        statusCode: StatusCodes.Status500InternalServerError,
        extensions: new Dictionary<string, object?>
        {
            ["code"] = ServiceHub.Core.Constants.ErrorCodes.UnexpectedFailure,
            ["correlationId"] = context.TraceIdentifier
        }).ExecuteAsync(context).ConfigureAwait(false);
}));

app.UseStatusCodePages();

// Identity, in the order it is trusted: a platform-injected principal, then a validated bearer
// token, then a configured API key. Each is a no-op unless configured, and none of them gates a
// request — they only decide how the caller is named (rule R6). With none configured every request
// is "from this browser session".
app.UseMiddleware<EasyAuthMiddleware>();
app.UseMiddleware<OidcBearerAuthenticationMiddleware>();
app.UseMiddleware<ApiKeyIdentityMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthEndpoints();
app.MapControllers();

// The SPA. One application, at the origin root, with no prefix — ADR-0014 D3 removed the /new
// mount, the cutover and the basename juggling along with it.
app.MapServiceHubSpa();

await app.RunAsync().ConfigureAwait(false);

/// <summary>Entry point marker, so integration tests can host the application.</summary>
public partial class Program;
