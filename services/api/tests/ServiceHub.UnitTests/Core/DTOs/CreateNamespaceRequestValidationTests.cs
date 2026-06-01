using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Enums;

namespace ServiceHub.UnitTests.Core.DTOs;

public sealed class CreateNamespaceRequestValidationTests
{
    private static IList<ValidationResult> Validate(CreateNamespaceRequest request)
    {
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, context, results, validateAllProperties: true);
        // Also run IValidatableObject.Validate
        results.AddRange(request.Validate(context));
        return results;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AWS provider
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_AwsProvider_MissingRegion_ReturnsValidationError()
    {
        var request = new CreateNamespaceRequest("testqueue", "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", ConnectionAuthType.AwsAccessKey)
        {
            Provider = CloudProviderType.Aws,
            AwsRegion = null
        };

        var results = Validate(request);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(CreateNamespaceRequest.AwsRegion))
            && r.ErrorMessage!.Contains("required"));
    }

    [Fact]
    public void Validate_AwsProvider_InvalidRegionFormat_ReturnsValidationError()
    {
        var request = new CreateNamespaceRequest("testqueue", "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", ConnectionAuthType.AwsAccessKey)
        {
            Provider = CloudProviderType.Aws,
            AwsRegion = "invalid-region-format"
        };

        var results = Validate(request);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(CreateNamespaceRequest.AwsRegion))
            && r.ErrorMessage!.Contains("invalid"));
    }

    [Theory]
    [InlineData("us-east-1")]
    [InlineData("eu-west-2")]
    [InlineData("ap-southeast-1")]
    [InlineData("ca-central-1")]
    public void Validate_AwsProvider_ValidRegion_PassesValidation(string region)
    {
        var request = new CreateNamespaceRequest("testqueue", "AKIAIOSFODNN7EXAMPLE:key", ConnectionAuthType.AwsAccessKey)
        {
            Provider = CloudProviderType.Aws,
            AwsRegion = region
        };

        var results = Validate(request);

        results.Should().NotContain(r => r.MemberNames.Contains(nameof(CreateNamespaceRequest.AwsRegion)));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GCP provider
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_GcpProvider_MissingProjectId_ReturnsValidationError()
    {
        var request = new CreateNamespaceRequest("my-subscription", "{\"type\":\"service_account\"}", ConnectionAuthType.GcpServiceAccount)
        {
            Provider = CloudProviderType.Gcp,
            GcpProjectId = null
        };

        var results = Validate(request);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(CreateNamespaceRequest.GcpProjectId))
            && r.ErrorMessage!.Contains("required"));
    }

    [Theory]
    [InlineData("ab")]            // too short (< 6 chars)
    [InlineData("My Project")]    // uppercase + spaces
    [InlineData("123-project")]   // starts with digit
    public void Validate_GcpProvider_InvalidProjectIdFormat_ReturnsValidationError(string projectId)
    {
        var request = new CreateNamespaceRequest("my-sub", "{\"type\":\"service_account\"}", ConnectionAuthType.GcpServiceAccount)
        {
            Provider = CloudProviderType.Gcp,
            GcpProjectId = projectId
        };

        var results = Validate(request);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(CreateNamespaceRequest.GcpProjectId)));
    }

    [Theory]
    [InlineData("my-project-123")]
    [InlineData("servicehub-dev")]
    [InlineData("acmecorp")]
    public void Validate_GcpProvider_ValidProjectId_PassesValidation(string projectId)
    {
        var request = new CreateNamespaceRequest("my-sub", "{\"type\":\"service_account\"}", ConnectionAuthType.GcpServiceAccount)
        {
            Provider = CloudProviderType.Gcp,
            GcpProjectId = projectId
        };

        var results = Validate(request);

        results.Should().NotContain(r => r.MemberNames.Contains(nameof(CreateNamespaceRequest.GcpProjectId)));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Azure provider
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_AzureProvider_MissingConnectionString_ReturnsValidationError()
    {
        var request = new CreateNamespaceRequest("testns", null, ConnectionAuthType.ConnectionString)
        {
            Provider = CloudProviderType.Azure
        };

        var results = Validate(request);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(CreateNamespaceRequest.ConnectionString)));
    }

    [Fact]
    public void Validate_AzureProvider_ManagedIdentity_NoConnectionStringRequired()
    {
        var request = new CreateNamespaceRequest("testns", null, ConnectionAuthType.ManagedIdentity)
        {
            Provider = CloudProviderType.Azure
        };

        var results = Validate(request);

        // ConnectionString is optional for managed identity
        results.Should().NotContain(r => r.MemberNames.Contains(nameof(CreateNamespaceRequest.ConnectionString)));
    }
}
