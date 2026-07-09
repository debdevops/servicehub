using System.Text.Json;
using System.Text.RegularExpressions;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Validation;

/// <summary>
/// Validates the plaintext credential format for non-Azure providers before encryption,
/// with the same rigor Azure connection strings get from IServiceBusClientFactory.
/// AWS credentials are stored as <c>AccessKeyId:SecretAccessKey</c>; GCP credentials
/// are the raw Service Account JSON key. Both ride the encrypted ConnectionString field.
/// </summary>
public static class CloudCredentialValidator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    // Long-term IAM access key IDs: 20 characters, "AKIA" prefix.
    private static readonly Regex AccessKeyIdRegex =
        new(@"^AKIA[0-9A-Z]{16}$", RegexOptions.None, RegexTimeout);

    // Secret access keys: exactly 40 base64-alphabet characters.
    private static readonly Regex SecretAccessKeyRegex =
        new(@"^[A-Za-z0-9/+=]{40}$", RegexOptions.None, RegexTimeout);

    /// <summary>
    /// Validates an AWS credential pair in the <c>AccessKeyId:SecretAccessKey</c> format
    /// expected by <c>AwsClientFactory</c>. Temporary (ASIA) session keys are rejected
    /// because the stored pair carries no session token.
    /// </summary>
    /// <param name="credential">The plaintext credential pair.</param>
    /// <returns>Success when the pair is well-formed; a validation error otherwise.</returns>
    public static Result ValidateAwsAccessKeyPair(string credential)
    {
        var parts = credential.Split(':', 2);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "AWS credentials must be in the format 'AccessKeyId:SecretAccessKey'."));
        }

        var accessKeyId = parts[0].Trim();
        var secretKey = parts[1].Trim();

        if (accessKeyId.StartsWith("ASIA", StringComparison.Ordinal))
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "Temporary session credentials (ASIA access keys) are not supported — use a long-term IAM access key (AKIA prefix)."));
        }

        if (!AccessKeyIdRegex.IsMatch(accessKeyId))
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "AWS access key ID must be 20 characters starting with 'AKIA'."));
        }

        if (!SecretAccessKeyRegex.IsMatch(secretKey))
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "AWS secret access key must be exactly 40 characters (letters, digits, '/', '+', '=')."));
        }

        return Result.Success();
    }

    /// <summary>
    /// Validates a GCP Service Account JSON key: well-formed JSON with the fields
    /// <c>GoogleCredential.FromStream</c> requires (<c>type</c> of <c>service_account</c>,
    /// <c>project_id</c>, <c>client_email</c>, and a PEM <c>private_key</c>).
    /// </summary>
    /// <param name="json">The plaintext Service Account JSON key.</param>
    /// <returns>Success when the key is well-formed; a validation error otherwise.</returns>
    public static Result ValidateGcpServiceAccountJson(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "GCP Service Account key is not valid JSON."));
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Result.Failure(Error.Validation(
                    ErrorCodes.Namespace.ConnectionStringInvalid,
                    "GCP Service Account key must be a JSON object."));
            }

            if (GetStringProperty(root, "type") != "service_account")
            {
                return Result.Failure(Error.Validation(
                    ErrorCodes.Namespace.ConnectionStringInvalid,
                    "GCP Service Account key must have \"type\": \"service_account\"."));
            }

            if (string.IsNullOrWhiteSpace(GetStringProperty(root, "project_id")))
            {
                return Result.Failure(Error.Validation(
                    ErrorCodes.Namespace.ConnectionStringInvalid,
                    "GCP Service Account key is missing \"project_id\"."));
            }

            if (GetStringProperty(root, "client_email") is not string email
                || !email.Contains('@', StringComparison.Ordinal))
            {
                return Result.Failure(Error.Validation(
                    ErrorCodes.Namespace.ConnectionStringInvalid,
                    "GCP Service Account key is missing a valid \"client_email\"."));
            }

            if (GetStringProperty(root, "private_key") is not string pem
                || !pem.Contains("PRIVATE KEY", StringComparison.Ordinal))
            {
                return Result.Failure(Error.Validation(
                    ErrorCodes.Namespace.ConnectionStringInvalid,
                    "GCP Service Account key is missing a PEM \"private_key\"."));
            }
        }

        return Result.Success();
    }

    private static string? GetStringProperty(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
