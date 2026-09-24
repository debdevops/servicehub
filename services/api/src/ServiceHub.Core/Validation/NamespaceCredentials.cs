using ServiceHub.Core.Constants;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;

namespace ServiceHub.Core.Validation;

/// <summary>
/// Checks that the credential offered for a namespace is one its provider can use — before
/// anything is stored, so ServiceHub never keeps a credential it could have refused.
/// </summary>
/// <remarks>
/// Provider-specific by nature, which is why it lives in Core beside the credential formats rather
/// than in a controller: callers ask "is this credential acceptable?" and never learn which cloud
/// they are talking to (rule R4). An Azure connection string is checked by
/// <c>Namespace.Create</c> itself.
/// </remarks>
public static class NamespaceCredentials
{
    /// <summary>Validates the auth type and credential against the provider they were given for.</summary>
    public static Result Validate(CloudProviderType provider, ConnectionAuthType authType, string? credential)
    {
        if (authType == ConnectionAuthType.AwsAccessKey && provider != CloudProviderType.Aws)
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "Authentication type 'AwsAccessKey' is only valid for an AWS namespace."));
        }

        if (authType == ConnectionAuthType.GcpServiceAccount && provider != CloudProviderType.Gcp)
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "Authentication type 'GcpServiceAccount' is only valid for a GCP namespace."));
        }

        if (string.IsNullOrEmpty(credential))
        {
            return Result.Success();
        }

        return (provider, authType) switch
        {
            (CloudProviderType.Aws, ConnectionAuthType.AwsAccessKey) => CloudCredentialValidator.ValidateAwsAccessKeyPair(credential),
            (CloudProviderType.Gcp, ConnectionAuthType.GcpServiceAccount) => CloudCredentialValidator.ValidateGcpServiceAccountJson(credential),
            _ => Result.Success(),
        };
    }
}
