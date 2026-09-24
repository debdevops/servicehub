using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;

namespace ServiceHub.Providers.Gcp;

/// <summary>
/// Identifies this assembly, and states the one fact about this cloud that the rest of the product
/// is not allowed to guess.
/// </summary>
/// <remarks>
/// The adapter itself — connection factory, receiver, sender, health check — is copied from the
/// archive at unit 1.5 (PORTING-MAP P19). Until then this type exists so the project is
/// real, so <c>DependencyDirectionTests</c> has an assembly to check, and so the boundary is in
/// place before there is any code to put on the wrong side of it.
/// </remarks>
public static class ProviderAssemblyMarker
{
    /// <summary>Which cloud this assembly adapts.</summary>
    public static CloudProviderType Provider => CloudProviderType.Gcp;

    /// <summary>
    /// What this cloud can and cannot prove. <b>Every "may we say Verified?" decision in the
    /// product reads this, and never a provider name</b> (rule R4).
    /// </summary>
    public static ProviderCapabilities Capabilities => ProviderCapabilities.Gcp;
}
