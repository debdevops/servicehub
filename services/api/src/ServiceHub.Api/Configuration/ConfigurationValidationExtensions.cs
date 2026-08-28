using ServiceHub.Api.Middleware;
using ServiceHub.Api.Security;
using ServiceHub.Core.Models;

namespace ServiceHub.Api.Configuration;

/// <summary>
/// Registers strongly-typed, validated configuration options that fail fast at startup.
/// <para>
/// Validation is intentionally conservative: it only rejects values that are structurally
/// invalid (out-of-range, or a webhook enabled without a usable URL). Every configuration
/// ServiceHub ships — Development, Production and test hosts — satisfies these
/// rules, so this adds a safety net for operator mistakes without changing any existing
/// behaviour. <c>ValidateOnStart()</c> surfaces failures during host startup rather than on
/// first use.
/// </para>
/// </summary>
public static class ConfigurationValidationExtensions
{
    /// <summary>
    /// Adds validated options for the ServiceBus, RateLimit, AuthFailureThrottle, Webhooks, Oidc,
    /// and Audit Retention sections.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddServiceHubConfigurationValidation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ServiceBusOptions>()
            .Bind(configuration.GetSection(ServiceBusOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                o => o.MaxRetryDelayMs >= o.RetryDelayMs,
                "ServiceBus:MaxRetryDelayMs must be greater than or equal to ServiceBus:RetryDelayMs.")
            .ValidateOnStart();

        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection("RateLimit"))
            .Validate(o => o.MaxRequests >= 1, "RateLimit:MaxRequests must be at least 1.")
            .Validate(o => o.WindowDuration > TimeSpan.Zero, "RateLimit:WindowDuration must be greater than zero.")
            .ValidateOnStart();

        services.AddOptions<AuthFailureThrottleOptions>()
            .Bind(configuration.GetSection(AuthFailureThrottleOptions.SectionName))
            .Validate(o => o.Threshold >= 1, "Security:Authentication:FailureThrottle:Threshold must be at least 1.")
            .Validate(o => o.Window > TimeSpan.Zero, "Security:Authentication:FailureThrottle:Window must be greater than zero.")
            .ValidateOnStart();

        services.AddOptions<WebhookOptions>()
            .Bind(configuration.GetSection(WebhookOptions.SectionName))
            .Validate(
                o => !o.Enabled || IsUsableHttpUrl(o.Url),
                "Webhooks:Url must be a valid absolute http/https URL when Webhooks:Enabled is true.")
            .Validate(o => o.DlqSpikeThreshold >= 1, "Webhooks:DlqSpikeThreshold must be at least 1.")
            .Validate(o => o.CooldownSeconds >= 0, "Webhooks:CooldownSeconds must be non-negative.")
            .Validate(
                o => Enum.IsDefined(o.Format),
                "Webhooks:Format must be one of Generic, Slack, Teams.")
            .Validate(
                o => o.PublicUrl is null || IsUsableHttpUrl(o.PublicUrl),
                "Webhooks:PublicUrl must be a valid absolute http/https URL when set.")
            .ValidateOnStart();

        services.AddOptions<OidcOptions>()
            .Bind(configuration.GetSection(OidcOptions.SectionName))
            .Validate(
                o => !o.Enabled || IsHttpsUrl(o.Authority),
                "Security:Oidc:Authority must be a valid absolute HTTPS URL when Security:Oidc:Enabled is true — " +
                "the discovery document and signing keys fetched from it must not be interceptable.")
            .Validate(
                o => !o.Enabled || !string.IsNullOrWhiteSpace(o.Audience),
                "Security:Oidc:Audience is required when Security:Oidc:Enabled is true.")
            .Validate(o => o.ClockSkewSeconds >= 0, "Security:Oidc:ClockSkewSeconds must be non-negative.")
            .ValidateOnStart();

        services.AddOptions<AuditRetentionOptions>()
            .Bind(configuration.GetSection(AuditRetentionOptions.SectionName))
            .Validate(
                o => !o.Enabled || o.RetentionDays >= 1,
                "Audit:Retention:RetentionDays must be at least 1 when Audit:Retention:Enabled is true.")
            .Validate(
                o => o.SweepIntervalHours >= 1,
                "Audit:Retention:SweepIntervalHours must be at least 1.")
            .ValidateOnStart();

        services.AddOptions<BackupOptions>()
            .Bind(configuration.GetSection(BackupOptions.SectionName))
            .Validate(
                o => o.ScheduledBackupIntervalHours >= 0,
                "Backup:ScheduledBackupIntervalHours must be non-negative (0 disables scheduled backups).")
            .Validate(
                o => o.RetentionCount >= 1,
                "Backup:RetentionCount must be at least 1.")
            .ValidateOnStart();

        return services;
    }

    private static bool IsUsableHttpUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsHttpsUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
