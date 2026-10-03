using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.UnitTests.Api;

/// <summary>
/// An agent's activity timeline is read from the ledger by actor, and the ledger is one chain across every namespace. A caller limited to
/// some namespaces (an OIDC token with namespace scopes) must not receive it — it names failures and queues outside the allow-list.
/// Copilot flagged this class of leak on four earlier pull requests; this pins it for the 4.1.0 code.
/// </summary>
public sealed class AgentActivityScopeTests
{
    private static AgentRuntimeState State() => new(
        new AgentDescriptor("auto-replay", "Auto Replay", "Retries what a rule names.", AgentKind.Act, AgentAuthority.ActsAutonomously, TimeSpan.FromSeconds(30), LedgerActor: "AutoReplay"),
        AgentHealth.Healthy, null, null, null, 0, false);

    private static (AgentsController Controller, Mock<IRecoveryQueries> Queries) Build(IReadOnlySet<Guid>? allowed)
    {
        var registry = new Mock<IAgentRegistry>();
        registry.Setup(r => r.StateOf("auto-replay")).Returns(State());
        registry.Setup(r => r.RecentCycles("auto-replay")).Returns([]);
        var audit = new Mock<IAuditTrail>();
        audit.Setup(a => a.QueryAsync(It.IsAny<AuditQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AuditPage([], 0));
        var queries = new Mock<IRecoveryQueries>();
        queries.Setup(q => q.EventsByActorAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        queries.Setup(q => q.DescribeSignaturesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        var http = new DefaultHttpContext();
        if (allowed is not null) http.Items["AllowedNamespaceIds"] = allowed;
        var controller = new AgentsController(registry.Object, audit.Object, queries.Object) { ControllerContext = new ControllerContext { HttpContext = http } };
        return (controller, queries);
    }

    [Fact]
    public async Task A_caller_limited_to_some_namespaces_is_not_given_the_agents_ledger_timeline()
    {
        var (controller, queries) = Build(new HashSet<Guid> { Guid.NewGuid() });

        (await controller.Activity("auto-replay", CancellationToken.None)).Should().BeOfType<OkObjectResult>();

        queries.Verify(q => q.EventsByActorAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        queries.Verify(q => q.DescribeSignaturesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_unrestricted_caller_still_gets_the_ledger_timeline()
    {
        var (controller, queries) = Build(null);

        (await controller.Activity("auto-replay", CancellationToken.None)).Should().BeOfType<OkObjectResult>();

        queries.Verify(q => q.EventsByActorAsync(It.IsAny<string>(), "AutoReplay", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
