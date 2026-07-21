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
/// cloud SDK dependency, so Simulator-seeded AWS/GCP DLQ messages get the same provider-aware
/// classification as messages from a real, flag-enabled namespace.
/// </para>
/// </summary>
public sealed class ForensicEngineRouter : IForensicEngineRouter
{
    private readonly IKeyedServiceProvider _keyedServices;

    /// <summary>
    /// Initialises a new instance of <see cref="ForensicEngineRouter"/>.
    /// </summary>
    /// <param name="keyedServices">
    /// The application's keyed service provider. The default ASP.NET Core container implements
    /// this automatically, so no special registration is required to inject it.
    /// </param>
    public ForensicEngineRouter(IKeyedServiceProvider keyedServices)
    {
        _keyedServices = keyedServices ?? throw new ArgumentNullException(nameof(keyedServices));
    }

    /// <inheritdoc />
    public ForensicEngineResult Analyse(DlqMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var engine = _keyedServices.GetKeyedService<IForensicEngine>(message.CloudProvider)
            ?? _keyedServices.GetRequiredKeyedService<IForensicEngine>(CloudProviderType.Azure);

        return engine.Analyse(message);
    }
}
