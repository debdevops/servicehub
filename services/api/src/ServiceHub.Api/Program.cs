using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Extensions;
using ServiceHub.Api.Middleware;
using ServiceHub.Infrastructure.Agents;

var builder = WebApplication.CreateBuilder(args);

// ── Services ────────────────────────────────────────────────────────────────────────────────────
// The composition root, and the ONLY place ServiceHub.Infrastructure and the ServiceHub.Providers.*
// projects meet (ADR-0014 D5).

builder.Services.AddControllers();
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
builder.Services.AddOpenApi();

// The agent platform runs with zero agents in Wave 0 and says so at startup. Agents arrive one
// file and one registration line at a time, from unit 2.1 onwards.
builder.Services.AddAgentPlatform();

var app = builder.Build();

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
