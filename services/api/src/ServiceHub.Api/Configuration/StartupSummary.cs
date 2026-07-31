namespace ServiceHub.Api.Configuration;

/// <summary>
/// Emits a single, secret-free summary of the effective operational configuration at startup.
/// This makes "how is this instance actually configured?" answerable from the first lines of
/// the log — invaluable when triaging a self-hosted deployment — without ever printing a
/// connection string, key, or other sensitive value (only booleans and provider names).
/// </summary>
public static class StartupSummary
{
    /// <summary>Logs the effective configuration summary for the running instance.</summary>
    /// <param name="app">The web application.</param>
    public static void LogStartupSummary(this WebApplication app)
    {
        var cfg = app.Configuration;
        var logger = app.Logger;

        var providers = new List<string> { "Azure" };
        if (cfg.GetValue("CloudProviders:Aws:Enabled", false)) providers.Add("AWS");
        if (cfg.GetValue("CloudProviders:Gcp:Enabled", false)) providers.Add("GCP");

        var otlpConfigured = !string.IsNullOrWhiteSpace(cfg["OpenTelemetry:Otlp:Endpoint"])
                             || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));

        logger.LogInformation(
            "ServiceHub starting — Environment={Environment}, Providers=[{Providers}], " +
            "AuthEnabled={AuthEnabled}, ConnectionStringEncryption={Encryption}, " +
            "RateLimiting={RateLimiting}, Webhooks={Webhooks}, " +
            "AppInsights={AppInsights}, OpenTelemetry={OpenTelemetry}",
            app.Environment.EnvironmentName,
            string.Join(", ", providers),
            cfg.GetValue("Security:Authentication:Enabled", false),
            cfg.GetValue("Security:EnableConnectionStringEncryption", true),
            !app.Environment.IsDevelopment(),
            cfg.GetValue("Webhooks:Enabled", false),
            !string.IsNullOrWhiteSpace(cfg["ApplicationInsights:ConnectionString"]),
            cfg.GetValue("OpenTelemetry:Enabled", false) || otlpConfigured);
    }
}
