using ServiceHub.Core.Entities;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Dispatches forensic analysis to the <see cref="IForensicEngine"/> registered for a message's
/// <see cref="DlqMessage.CloudProvider"/>, falling back to the Azure-oriented base engine when no
/// provider-specific engine is registered (e.g. a cloud provider whose live messaging flag is
/// disabled). This is the seam <see cref="ServiceHub.Core.Interfaces.IDlqMonitorService"/>
/// implementations should depend on instead of <see cref="IForensicEngine"/> directly, so that
/// AWS/GCP-specific rules are consulted without every caller needing to know how the engines are
/// composed.
/// </summary>
public interface IForensicEngineRouter
{
    /// <summary>
    /// Analyses a DLQ message using the forensic engine appropriate for its cloud provider.
    /// </summary>
    ForensicEngineResult Analyse(DlqMessage message);
}
