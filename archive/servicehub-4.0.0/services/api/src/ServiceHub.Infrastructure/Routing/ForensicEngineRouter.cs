using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Routing;

/// <summary>
/// Resolves the <see cref="IForensicEngine"/> keyed-registered for a message's
/// <see cref="DlqMessage.CloudProvider"/> (see <see cref="CloudProviderType"/>), falling back to
/// the Azure-oriented base engine when no provider-specific engine is registered.
/// <para>
/// Provider-specific engines (<c>AwsForensicEngine</c>, <c>GcpForensicEngine</c>) are registered
/// unconditionally by <c>ServiceHub.Api</c>'s composition root, independent of whether the live
/// AWS/GCP messaging provider flag is enabled — they are pure, stateless classifiers with no
/// cloud SDK dependency.
/// </para>
/// </summary>
public sealed class ForensicEngineRouter : IForensicEngineRouter
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initialises a new instance of <see cref="ForensicEngineRouter"/>.
    /// </summary>
    /// <param name="serviceProvider">
    /// The application's service provider. Keyed resolution is done via the
    /// <c>GetKeyedService</c>/<c>GetRequiredKeyedService</c> extension methods, which the default
    /// ASP.NET Core container supports on any <see cref="IServiceProvider"/> — unlike
    /// <see cref="IKeyedServiceProvider"/>, <see cref="IServiceProvider"/> is always
    /// constructor-injectable without extra registration.
    /// </param>
    public ForensicEngineRouter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public ForensicEngineResult Analyse(DlqMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var engine = _serviceProvider.GetKeyedService<IForensicEngine>(message.CloudProvider)
            ?? _serviceProvider.GetRequiredKeyedService<IForensicEngine>(CloudProviderType.Azure);

        return engine.Analyse(message);
    }
}
