using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Providers.Aws;

/// <summary>
/// Extension methods for registering AWS SQS/SNS infrastructure services.
/// Call <see cref="AddAwsProvider"/> once from the host application's DI setup.
/// </summary>
public static class AwsDependencyInjection
{
    /// <summary>
    /// Registers the AWS SQS/SNS messaging provider and all its dependencies.
    /// <para>
    /// Note: <see cref="AwsMessageReceiver"/> and <see cref="AwsMessageSender"/> are registered
    /// as concrete types (not as <c>IMessageReceiver</c>/<c>IMessageSender</c>) to avoid
    /// shadowing the Azure provider registration, which owns those interfaces.
    /// <see cref="AwsMessagingProvider"/> resolves them by concrete type via constructor injection.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAwsProvider(this IServiceCollection services)
    {
        // AwsClientFactory is a singleton — AWS SDK clients are thread-safe and expensive to create.
        services.TryAddSingleton<IAwsClientFactory, AwsClientFactory>();

        // Register concrete types — NOT as IMessageReceiver/IMessageSender (those belong to Azure).
        services.AddScoped<AwsMessageReceiver>();
        services.AddScoped<AwsMessageSender>();

        // DLQ detector — used by background monitoring if extended to multi-cloud.
        services.AddScoped<AwsDlqDetector>();

        // Register the provider as ICloudMessagingProvider (TryAddEnumerable prevents duplicates).
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<ICloudMessagingProvider, AwsMessagingProvider>());

        // DlqObserver/DynamoDbObserverLogReader is deliberately NOT registered: the observer is a
        // later unit, and until it is wired nothing may claim an observer is attached.

        // Register the AWS health check so the /health/dependencies endpoint validates SQS
        // connectivity. Tagged "dependencies", not "ready" — an unreachable AWS namespace is an
        // external broker outage and must never flip /health/ready to Unhealthy.
        services.AddHealthChecks()
            .AddCheck<AwsHealthCheck>("aws-connectivity", tags: ["aws", "dependencies"]);

        return services;
    }
}
