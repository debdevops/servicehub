using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Aws;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.Aws;

/// <summary>
/// Tests for <see cref="AwsClientFactory"/> credential resolution — in particular the
/// fail-closed contract: a namespace whose auth type is not an AWS auth type must raise
/// a configuration error instead of silently using anonymous or ambient credentials.
/// </summary>
public sealed class AwsClientFactoryTests
{
    private static AwsClientFactory BuildFactory()
    {
        // Pass-through protector: unprotect returns the stored value unchanged,
        // mirroring ConnectionStringProtector's behaviour for unprefixed plaintext.
        var protector = new Mock<IConnectionStringProtector>();
        protector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns<string>(Result.Success);

        return new AwsClientFactory(protector.Object, NullLogger<AwsClientFactory>.Instance);
    }

    [Fact]
    public void GetSqsClient_WithAccessKeyNamespace_CreatesClient()
    {
        var ns = Namespace.Create(
            "sqs.us-east-1.amazonaws.com",
            "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
            provider: CloudProviderType.Aws,
            awsRegion: "us-east-1").Value;

        var client = BuildFactory().GetSqsClient(ns);

        client.Should().NotBeNull();
    }

    [Fact]
    public void GetSqsClient_WithNonAwsAuthType_FailsClosed()
    {
        // A namespace record carrying a non-AWS auth type is a configuration error;
        // it must never resolve to anonymous or ambient credentials.
        var ns = Namespace.CreateWithManagedIdentity(
            "test-aws-mi",
            ConnectionAuthType.ManagedIdentity,
            provider: CloudProviderType.Aws,
            awsRegion: "us-east-1").Value;

        var act = () => BuildFactory().GetSqsClient(ns);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not a supported AWS auth type*");
    }

    [Fact]
    public void GetSnsClient_WithNonAwsAuthType_FailsClosed()
    {
        var ns = Namespace.CreateWithManagedIdentity(
            "test-aws-mi-sns",
            ConnectionAuthType.ManagedIdentity,
            provider: CloudProviderType.Aws,
            awsRegion: "us-east-1").Value;

        var act = () => BuildFactory().GetSnsClient(ns);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not a supported AWS auth type*");
    }

    [Fact]
    public void GetSqsClient_AccessKeyNamespace_UnprotectsBeforeParsing()
    {
        var protector = new Mock<IConnectionStringProtector>();
        protector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns(Result.Success("AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"));
        var factory = new AwsClientFactory(protector.Object, NullLogger<AwsClientFactory>.Instance);

        var ns = Namespace.Create(
            "sqs.us-east-1.amazonaws.com",
            "ENC:V2:pretend-ciphertext",
            provider: CloudProviderType.Aws,
            awsRegion: "us-east-1").Value;

        var client = factory.GetSqsClient(ns);

        client.Should().NotBeNull();
        protector.Verify(p => p.Unprotect("ENC:V2:pretend-ciphertext"), Times.Once);
    }
}
