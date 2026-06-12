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
    DefaultAzureCredential = 3
}
