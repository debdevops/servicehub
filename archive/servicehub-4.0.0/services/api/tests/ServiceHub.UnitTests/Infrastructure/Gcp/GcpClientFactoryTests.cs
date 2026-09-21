using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Gcp;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.Gcp;

/// <summary>
/// Tests for <see cref="GcpClientFactory"/> credential resolution — in particular the
/// fail-closed contract: a bad Service Account key or a non-GCP auth type must raise a
/// configuration error instead of silently falling back to Application Default Credentials.
/// </summary>
public sealed class GcpClientFactoryTests
{
    private static GcpClientFactory BuildFactory()
    {
        // Pass-through protector: unprotect returns the stored value unchanged,
        // mirroring ConnectionStringProtector's behaviour for unprefixed plaintext.
        var protector = new Mock<IConnectionStringProtector>();
        protector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns<string>(Result.Success);

        return new GcpClientFactory(protector.Object, NullLogger<GcpClientFactory>.Instance);
    }

    private static Namespace ServiceAccountNamespace(string connectionString) =>
        Namespace.Create(
            "my-gcp-project",
            connectionString,
            provider: CloudProviderType.Gcp,
            gcpProjectId: "my-gcp-project").Value;

    private static string ValidServiceAccountJson()
    {
        using var rsa = RSA.Create(2048);
        var pem = new string(PemEncoding.Write("PRIVATE KEY", rsa.ExportPkcs8PrivateKey()));
        var escapedPem = pem.Replace("\r", string.Empty).Replace("\n", "\\n");
        return $$"""
        {
          "type": "service_account",
          "project_id": "my-gcp-project",
          "private_key_id": "key-1",
          "private_key": "{{escapedPem}}",
          "client_email": "svc@my-gcp-project.iam.gserviceaccount.com",
          "token_uri": "https://oauth2.googleapis.com/token"
        }
        """;
    }

    [Fact]
    public async Task GetSubscriberClientAsync_WithValidServiceAccountKey_CreatesClient()
    {
        var ns = ServiceAccountNamespace(ValidServiceAccountJson());

        var client = await BuildFactory().GetSubscriberClientAsync(ns, "sub-1", CancellationToken.None);

        client.Should().NotBeNull();
    }

    [Fact]
    public async Task GetSubscriberClientAsync_WithUnparsableServiceAccountKey_FailsClosed()
    {
        // Previously this fell back to Application Default Credentials — the host's own
        // identity. It must surface as a configuration error instead.
        var ns = ServiceAccountNamespace("{ \"type\": \"service_account\", \"broken\": true }");

        var act = async () => await BuildFactory().GetSubscriberClientAsync(ns, "sub-1", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not a valid Google credential*");
    }

    [Fact]
    public async Task GetSubscriberClientAsync_WithNonGcpAuthType_FailsClosed()
    {
        var ns = Namespace.CreateWithManagedIdentity(
            "test-gcp-mi",
            ConnectionAuthType.ManagedIdentity,
            provider: CloudProviderType.Gcp,
            gcpProjectId: "my-gcp-project").Value;

        var act = async () => await BuildFactory().GetSubscriberClientAsync(ns, "sub-1", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not a supported GCP auth type*");
    }

    [Fact]
    public async Task GetTopicAdminClientAsync_WithDecryptFailure_FailsClosed()
    {
        var protector = new Mock<IConnectionStringProtector>();
        protector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns(Result.Failure<string>(Error.Validation("Enc.Failed", "bad key")));
        var factory = new GcpClientFactory(protector.Object, NullLogger<GcpClientFactory>.Instance);

        var ns = ServiceAccountNamespace("ENC:V2:pretend-ciphertext");

        var act = async () => await factory.GetTopicAdminClientAsync(ns, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Failed to decrypt*");
    }

    [Fact]
    public async Task RemoveClientAsync_EvictsCachedClients_SoNextGetCreatesFreshOnes()
    {
        var factory = BuildFactory();
        var ns = ServiceAccountNamespace(ValidServiceAccountJson());

        var publisherBefore = await factory.GetPublisherClientAsync(ns, "topic-1", CancellationToken.None);
        var subscriberBefore = await factory.GetSubscriberClientAsync(ns, "sub-1", CancellationToken.None);
        var topicAdminBefore = await factory.GetTopicAdminClientAsync(ns, CancellationToken.None);

        await factory.RemoveClientAsync(ns.Id, CancellationToken.None);

        var publisherAfter = await factory.GetPublisherClientAsync(ns, "topic-1", CancellationToken.None);
        var subscriberAfter = await factory.GetSubscriberClientAsync(ns, "sub-1", CancellationToken.None);
        var topicAdminAfter = await factory.GetTopicAdminClientAsync(ns, CancellationToken.None);

        publisherAfter.Should().NotBeSameAs(publisherBefore);
        subscriberAfter.Should().NotBeSameAs(subscriberBefore);
        topicAdminAfter.Should().NotBeSameAs(topicAdminBefore);
    }

    [Fact]
    public async Task RemoveClientAsync_WithNothingCached_DoesNotThrow()
    {
        var factory = BuildFactory();

        var act = async () => await factory.RemoveClientAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
