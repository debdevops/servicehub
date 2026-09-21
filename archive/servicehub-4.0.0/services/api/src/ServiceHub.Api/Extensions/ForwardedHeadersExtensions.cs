using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using ServiceHub.Api.Configuration;

namespace ServiceHub.Api.Extensions;

/// <summary>
/// Extension methods for configuring X-Forwarded-* header handling.
/// </summary>
public static class ForwardedHeadersExtensions
{
    /// <summary>
    /// Configures forwarded headers (X-Forwarded-*) for reverse proxy scenarios.
    /// Safe by default: headers are trusted ONLY if explicitly configured via
    /// ForwardedHeadersConfiguration or auto-detection (e.g., Azure App Service).
    /// </summary>
    public static IServiceCollection AddSecureForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var config = new ForwardedHeadersConfiguration();
        configuration.GetSection("ForwardedHeaders").Bind(config);

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            // Start with no trusted proxies (safe default)
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();

            var isEnabled = config.Enabled || ShouldAutoEnableForAzureAppService(environment, configuration, config);

            if (!isEnabled)
            {
                // Forwarded headers are disabled: don't trust any X-Forwarded-* headers
                options.ForwardedHeaders = ForwardedHeaders.None;
                return;
            }

            // Forwarded headers are enabled: configure which headers to trust
            var forwardedHeaders = ForwardedHeaders.None;
            if (config.UseXForwardedFor)
                forwardedHeaders |= ForwardedHeaders.XForwardedFor;
            if (config.UseXForwardedProto)
                forwardedHeaders |= ForwardedHeaders.XForwardedProto;

            options.ForwardedHeaders = forwardedHeaders;

            // Add individually-specified trusted proxies
            foreach (var proxy in config.KnownProxies)
            {
                if (IPAddress.TryParse(proxy, out var ipAddress))
                {
                    options.KnownProxies.Add(ipAddress);
                }
            }

            // Add trusted CIDR networks
            foreach (var network in config.KnownNetworks)
            {
                if (TryParseIPv4Network(network, out var ipAddress, out var prefixLength) && ipAddress != null)
                {
                    options.KnownIPNetworks.Add(new System.Net.IPNetwork(ipAddress, prefixLength));
                }
            }
        });

        return services;
    }

    /// <summary>
    /// Checks if Azure App Service auto-detection should enable forwarded headers.
    /// Returns true if:
    /// - AutoDetectAzureAppService is true
    /// - Running on Azure App Service (detected via WEBSITE_AUTH_ENABLED env var)
    /// </summary>
    private static bool ShouldAutoEnableForAzureAppService(
        IHostEnvironment environment,
        IConfiguration configuration,
        ForwardedHeadersConfiguration config)
    {
        if (!config.AutoDetectAzureAppService)
            return false;

        // Azure App Service sets WEBSITE_AUTH_ENABLED when Easy Auth is configured
        var websiteAuthEnabled = configuration["WEBSITE_AUTH_ENABLED"];
        return websiteAuthEnabled == "true" || websiteAuthEnabled == "True";
    }

    /// <summary>
    /// Parses an IPv4 CIDR notation (e.g., "10.0.0.0/8") into IPAddress and prefix length.
    /// </summary>
    private static bool TryParseIPv4Network(string cidr, out IPAddress? address, out int prefixLength)
    {
        address = null;
        prefixLength = 0;

        if (string.IsNullOrWhiteSpace(cidr))
            return false;

        var parts = cidr.Split('/');
        if (parts.Length != 2)
            return false;

        if (!IPAddress.TryParse(parts[0], out var tempAddress))
            return false;

        address = tempAddress;

        if (!int.TryParse(parts[1], out prefixLength) || prefixLength < 0 || prefixLength > 32)
            return false;

        return true;
    }
}
