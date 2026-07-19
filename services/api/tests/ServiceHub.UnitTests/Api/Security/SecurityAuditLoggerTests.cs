using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Api.Security;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.UnitTests.Api.Security;

/// <summary>
/// Regression pack for <see cref="SecurityAuditLogger"/> — the audit trail is a
/// security control, so these pin outcome normalisation, actor identity
/// resolution, and the log-forging defence (user-controlled values must never
/// carry newlines into the persisted trail).
/// </summary>
public sealed class SecurityAuditLoggerTests
{
    private static readonly Guid TestNamespaceId = Guid.NewGuid();

    private static (SecurityAuditLogger Sut, List<AuditLog> Captured) BuildSut()
    {
        var captured = new List<AuditLog>();
        var auditService = new Mock<IAuditService>();
        auditService.Setup(a => a.Enqueue(It.IsAny<AuditLog>()))
            .Callback<AuditLog>(captured.Add);
        var sut = new SecurityAuditLogger(NullLogger<SecurityAuditLogger>.Instance, auditService.Object);
        return (sut, captured);
    }

    private static DefaultHttpContext BuildHttpContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path = "/api/v1/namespaces/x/queues/orders/messages";
        ctx.Items["CorrelationId"] = "corr-123";
        return ctx;
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new SecurityAuditLogger(null!, Mock.Of<IAuditService>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullAuditService_Throws()
    {
        var act = () => new SecurityAuditLogger(NullLogger<SecurityAuditLogger>.Instance, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("Succeeded", "Success")]
    [InlineData("Failed", "Failure")]
    [InlineData("Denied", "Denied")]
    [InlineData("Partial", "Partial")]
    public void LogCriticalAction_NormalizesOutcomes(string given, string expected)
    {
        var (sut, captured) = BuildSut();

        sut.LogCriticalAction(BuildHttpContext(), "owner-1", "messages:send", given,
            TestNamespaceId, EnvironmentType.Dev, "orders");

        captured.Should().HaveCount(1);
        captured[0].Outcome.Should().Be(expected);
    }

    [Fact]
    public void LogCriticalAction_CapturesRequestAndActionFields()
    {
        var (sut, captured) = BuildSut();

        sut.LogCriticalAction(BuildHttpContext(), "owner-1", "messages:deadletter", "Succeeded",
            TestNamespaceId, EnvironmentType.Dev, entityName: "orders",
            cloudProvider: "AWS", sequenceNumber: 42, detail: "moved 3 messages");

        var entry = captured.Single();
        entry.OwnerId.Should().Be("owner-1");
        entry.Action.Should().Be("messages:deadletter");
        entry.NamespaceId.Should().Be(TestNamespaceId);
        entry.EntityName.Should().Be("orders");
        entry.CloudProvider.Should().Be("aws"); // normalised to lowercase
        entry.Environment.Should().Be("Dev");
        entry.SequenceNumber.Should().Be(42);
        entry.CorrelationId.Should().Be("corr-123");
        entry.HttpMethod.Should().Be("POST");
        entry.HttpPath.Should().Contain("/queues/orders/messages");
    }

    [Fact]
    public void LogCriticalAction_SanitisesNewlinesOutOfUserControlledValues()
    {
        var (sut, captured) = BuildSut();

        // A hostile entity name attempting to forge extra audit lines.
        sut.LogCriticalAction(BuildHttpContext(), "owner-1",
            "messages:send\nFORGED_LINE action=namespaces:delete", "Succeeded",
            TestNamespaceId, EnvironmentType.Dev,
            resourceName: "queue\r\nSECURITY_AUDIT outcome=Success");

        var entry = captured.Single();
        entry.Action.Should().NotContain("\n").And.NotContain("\r");
        entry.ResourceName.Should().NotContain("\n").And.NotContain("\r");
    }

    [Fact]
    public void LogCriticalAction_PrefersApiKeyIdentity()
    {
        var (sut, captured) = BuildSut();
        var ctx = BuildHttpContext();
        ctx.Items["ApiKeyName"] = "ci-pipeline";

        sut.LogCriticalAction(ctx, "owner-1", "messages:send", "Succeeded");

        captured.Single().UserIdentity.Should().Be("ApiKey:ci-pipeline");
    }

    [Fact]
    public void LogCriticalAction_FallsBackToOwnerIdIdentity()
    {
        var (sut, captured) = BuildSut();
        var ctx = BuildHttpContext();
        ctx.Items["OwnerId"] = "local-owner";

        sut.LogCriticalAction(ctx, "local-owner", "messages:send", "Succeeded");

        captured.Single().UserIdentity.Should().Be("local-owner");
    }
}
