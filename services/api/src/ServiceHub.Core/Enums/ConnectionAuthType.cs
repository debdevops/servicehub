namespace ServiceHub.Core.Enums;

/// <summary>
/// Represents the authentication type used to connect to an Azure Service Bus namespace.
/// </summary>
public enum ConnectionAuthType
{
    /// <summary>
    /// Connection using a connection string with Shared Access Signature (SAS) credentials.
    /// </summary>
    ConnectionString = 0,

    /// <summary>
    /// Connection using Azure Active Directory (Entra ID) managed identity.
    /// </summary>
    ManagedIdentity = 1,

    /// <summary>
    /// Connection using Azure Active Directory (Entra ID) service principal credentials.
    /// </summary>
    ServicePrincipal = 2,

    /// <summary>
    /// Connection using Azure Active Directory (Entra ID) default credentials chain.
    /// </summary>
    DefaultAzureCredential = 3,

    // AWS authentication types

    /// <summary>
    /// Connection using an AWS Access Key ID and Secret Access Key pair.
    /// </summary>
    AwsAccessKey = 10,

    /// <summary>
    /// Connection using an AWS IAM role attached to the hosting compute resource.
    /// </summary>
    AwsIamRole = 11,

    /// <summary>
    /// Connection using AWS OpenID Connect (OIDC) web identity federation.
    /// </summary>
    AwsOidc = 12,

    // GCP authentication types

    /// <summary>
    /// Connection using a GCP service account JSON key file.
    /// </summary>
    GcpServiceAccount = 20,

    /// <summary>
    /// Connection using GCP Workload Identity Federation (keyless authentication).
    /// </summary>
    GcpWorkloadIdentity = 21
}
