using System.Net;
using Azure.Identity;
using ServiceHub.Core.Results;
using ServiceHub.Core.Security;

namespace ServiceHub.Providers.Azure.Components;

/// <summary>Turns an Azure failure into the one error a screen can show. Shared so every Azure source says the same thing.</summary>
internal static class AzureFailures
{
    /// <summary>
    /// A refused or unsigned read becomes <c>Not enabled for this connection: ...</c> with what is needed; anything else is an external
    /// failure with the message redacted.
    /// </summary>
    /// <param name="ex">What went wrong.</param>
    /// <param name="code">The error code prefix, such as <c>Azure.LogicApps</c>.</param>
    /// <param name="needs">What the identity must be allowed to do, in a few words.</param>
    public static Error ToError(Exception ex, string code, string needs) => ex switch
    {
        AzureNotEntraException =>
            Error.Forbidden($"{code}.NotEnabled", "Not enabled for this connection: it signs in with a Service Bus connection string or a stored secret. Reading this needs an Azure connection that signs in with Entra (Managed Identity or Default Azure Credential)."),
        AzureRestException { Status: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
            Error.Forbidden($"{code}.NotEnabled", $"Not enabled for this connection: its identity may not read this ({needs})."),
        AuthenticationFailedException or CredentialUnavailableException =>
            Error.Forbidden($"{code}.NotEnabled", "Not enabled for this connection: it could not sign in to Azure with Entra. Check the identity this server runs as."),
        _ => Error.ExternalService($"{code}.ReadFailed", LogRedactor.SanitiseForLog(LogRedactor.Redact(ex.Message))),
    };
}
