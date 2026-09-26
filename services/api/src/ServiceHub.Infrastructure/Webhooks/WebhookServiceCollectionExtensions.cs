using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Infrastructure.Webhooks;

/// <summary>Slack, Teams and generic webhook delivery (unit 5.5), registered exactly as 4.0.0 did — SSRF guard included.</summary>
public static class WebhookServiceCollectionExtensions
{
    /// <summary>Binds <c>Webhooks</c>, registers the three formatters, the notifier with its pinned-address handler, and the escalation handler.</summary>
    public static IServiceCollection AddWebhooks(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.Configure<WebhookOptions>(opts => configuration?.GetSection(WebhookOptions.SectionName).Bind(opts));

        // One formatter per format; the notifier picks by WebhookOptions.Format.
        services.AddSingleton<IWebhookMessageFormatter, GenericWebhookFormatter>();
        services.AddSingleton<IWebhookMessageFormatter, SlackWebhookFormatter>();
        services.AddSingleton<IWebhookMessageFormatter, TeamsWebhookFormatter>();

        // A hostname that resolves to a loopback/private/link-local address is rejected just as an IP literal is.
        services.AddSingleton<IDnsResolver, DnsResolver>();

        services.AddHttpClient<IWebhookNotifier, WebhookNotifier>(client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                // Connect only to the address the SSRF guard validated — no second DNS lookup to rebind (WebhookConnectCallback).
                ConnectCallback = WebhookConnectCallback.ConnectAsync,

                // The guard validates WebhookOptions.Url only, never a Location header — so redirects are never followed.
                AllowAutoRedirect = false,
            });

        // Settings' channels (unit 6.3) share the same handler rules through one named client.
        services.AddHttpClient(WebhookChannelSender.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                ConnectCallback = WebhookConnectCallback.ConnectAsync,
                AllowAutoRedirect = false,
            });
        services.AddScoped<IEscalationDelivery, WebhookChannelSender>();

        services.AddSingleton<WebhookEscalationHandler>();
        return services;
    }
}
