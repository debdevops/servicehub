using ServiceHub.Api.Middleware;
using ServiceHub.Core.Models;

namespace ServiceHub.Api.Configuration;

/// <summary>
/// Registers strongly-typed, validated configuration options that fail fast at startup.
/// <para>
/// Validation is intentionally conservative: it only rejects values that are structurally
/// invalid (out-of-range, or a webhook enabled without a usable URL). Every configuration
/// ServiceHub ships — Development, Simulator, Production and test hosts — satisfies these
/// rules, so this adds a safety net for operator mistakes without changing any existing
/// behaviour. <c>ValidateOnStart()</c> surfaces failures during host startup rather than on
/// first use.
/// </para>
/// </summary>
public static class ConfigurationValidationExtensions
{
    /// <summary>
    /// Adds validated options for the ServiceBus, RateLimit and Webhooks sections.
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

        return services;
    }

    private static bool IsUsableHttpUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
