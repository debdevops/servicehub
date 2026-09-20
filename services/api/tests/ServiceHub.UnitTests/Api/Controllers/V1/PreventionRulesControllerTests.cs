using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

/// <summary>
/// P5 <c>PreventionRulesController</c> (PREVENTION-RULE-DESIGN-2026-08-29.md). Covers proposal
/// validation, the always-<c>ObserveOnly</c> Action guarantee, namespace/governance gating
/// (mirroring <c>RulesControllerTests</c>' pattern for <c>AutoReplayRule</c>), versioning via
/// <c>SupersedesRuleEntryId</c>, and revocation.
/// </summary>
public sealed class PreventionRulesControllerTests
{
    private const string OwnerId = "owner-a";

    private readonly Mock<IPlaybookLedger> _playbookLedger = new();
    private readonly Mock<IPreventionRuleEvaluationService> _evaluationService = new();
    private readonly Mock<INamespaceRepository> _namespaceRepository = new();
    private readonly Mock<IGovernanceAccessEvaluator> _governanceAccessEvaluator = new();
    private readonly PreventionRulesController _controller;

    public PreventionRulesControllerTests()
    {
        _governanceAccessEvaluator
            .Setup(e => e.EvaluateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GovernanceRole>(),
                It.IsAny<Guid?>(), It.IsAny<PillarKind?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _controller = new PreventionRulesController(
            _playbookLedger.Object, _evaluationService.Object, _namespaceRepository.Object, _governanceAccessEvaluator.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Items = { { "OwnerId", OwnerId } }
                }
            }
        };
    }

    private static Namespace CreateNamespace() =>
        Namespace.Create(
            "orders-ns",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            environment: EnvironmentType.Dev,
            provider: CloudProviderType.Azure,
            ownerId: OwnerId).Value;

    private void SetupOwnedNamespace(Namespace ns) =>
        _namespaceRepository
            .Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

    private static ProposePreventionRuleRequest ValidRequest(Guid namespaceId, Guid? supersedesRuleEntryId = null) => new(
        Name: "Test rule",
        NamespaceId: namespaceId,
        EntityName: "orders-queue",
        DriftFindingType: "SchemaShapeDrift",
        MinSeverity: 70,
        MinOccurrences: 1,
        WindowHours: 24,
        RuleExpiresAt: DateTimeOffset.UtcNow.AddDays(30),
        SupersedesRuleEntryId: supersedesRuleEntryId,
        Justification: "Testing.");

    private static PlaybookEntry BuildRuleEntry(
        Guid namespaceId, PlaybookEntryState state = PlaybookEntryState.Approved, PreventionRuleProposal? rule = null)
    {
        rule ??= new PreventionRuleProposal(
            Guid.NewGuid(), 1, "Test rule", "orders-queue",
            new PreventionRuleCondition("SchemaShapeDrift", 70, 1, 24),
            null, DateTimeOffset.UtcNow.AddDays(30), PreventionRuleActions.ObserveOnly);

        return new PlaybookEntry
        {
            OwnerId = OwnerId,
            PillarKind = PillarKind.Prevent,
            ProposalKind = "PreventionRuleProposal",
            EvidenceRefJson = "{}",
            ProposalJson = JsonSerializer.Serialize(rule),
            ProposedAt = DateTimeOffset.UtcNow,
            ProposerIdentity = "alex@contoso.com",
            ProposerKind = PlaybookActorKind.User,
            NamespaceId = namespaceId,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(14),
            State = state,
            Disposition = state == PlaybookEntryState.Approved ? PlaybookDisposition.Approved : null,
        };
    }

    // ── Constructor ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullPlaybookLedger_Throws()
    {
        var act = () => new PreventionRulesController(
            null!, _evaluationService.Object, _namespaceRepository.Object, _governanceAccessEvaluator.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("playbookLedger");
    }

    [Fact]
    public void Constructor_NullEvaluationService_Throws()
    {
        var act = () => new PreventionRulesController(
            _playbookLedger.Object, null!, _namespaceRepository.Object, _governanceAccessEvaluator.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("evaluationService");
    }

    [Fact]
    public void Constructor_NullNamespaceRepository_Throws()
    {
        var act = () => new PreventionRulesController(
            _playbookLedger.Object, _evaluationService.Object, null!, _governanceAccessEvaluator.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("namespaceRepository");
    }

    [Fact]
    public void Constructor_NullGovernanceAccessEvaluator_Throws()
    {
        var act = () => new PreventionRulesController(
            _playbookLedger.Object, _evaluationService.Object, _namespaceRepository.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("governanceAccessEvaluator");
    }

    // ── Propose ─────────────────────────────────────────────────────

    [Fact]
    public async Task Propose_ValidRequest_ProposesActionObserveOnly_RegardlessOfCallerInput()
    {
        var ns = CreateNamespace();
        SetupOwnedNamespace(ns);

        ProposePlaybookEntryRequest? captured = null;
        _playbookLedger
            .Setup(l => l.ProposeAsync(It.IsAny<ProposePlaybookEntryRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProposePlaybookEntryRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(Result<PlaybookEntry>.Success(BuildRuleEntry(ns.Id, PlaybookEntryState.Proposed)));

        var result = await _controller.Propose(ValidRequest(ns.Id));

        result.Result.Should().BeOfType<CreatedResult>();
        captured.Should().NotBeNull();
        captured!.ProposalKind.Should().Be("PreventionRuleProposal");
        captured.PillarKind.Should().Be(PillarKind.Prevent);

        using var doc = JsonDocument.Parse(captured.ProposalJson);
        doc.RootElement.GetProperty("Action").GetString().Should().Be(PreventionRuleActions.ObserveOnly);
    }

    [Theory]
    [InlineData("NotARealType")]
    [InlineData("None")]
    [InlineData("")]
    public async Task Propose_InvalidDriftFindingType_ReturnsValidationError(string driftFindingType)
    {
        var ns = CreateNamespace();
        SetupOwnedNamespace(ns);

        var request = ValidRequest(ns.Id) with { DriftFindingType = driftFindingType };
        var result = await _controller.Propose(request);

        result.Result.Should().NotBeOfType<CreatedResult>();
        _playbookLedger.Verify(l => l.ProposeAsync(It.IsAny<ProposePlaybookEntryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Propose_MinSeverityOutOfRange_ReturnsValidationError(int minSeverity)
    {
        var ns = CreateNamespace();
        SetupOwnedNamespace(ns);

        var request = ValidRequest(ns.Id) with { MinSeverity = minSeverity };
        var result = await _controller.Propose(request);

        result.Result.Should().NotBeOfType<CreatedResult>();
    }

    [Fact]
    public async Task Propose_MinOccurrencesLessThanOne_ReturnsValidationError()
    {
        var ns = CreateNamespace();
        SetupOwnedNamespace(ns);

        var request = ValidRequest(ns.Id) with { MinOccurrences = 0 };
        var result = await _controller.Propose(request);

        result.Result.Should().NotBeOfType<CreatedResult>();
    }

    [Fact]
    public async Task Propose_RuleExpiresAtInThePast_ReturnsValidationError()
    {
        var ns = CreateNamespace();
        SetupOwnedNamespace(ns);

        var request = ValidRequest(ns.Id) with { RuleExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) };
        var result = await _controller.Propose(request);

        result.Result.Should().NotBeOfType<CreatedResult>();
    }

    [Fact]
    public async Task Propose_EmptyEntityName_ReturnsValidationError()
    {
        var ns = CreateNamespace();
        SetupOwnedNamespace(ns);

        var request = ValidRequest(ns.Id) with { EntityName = "   " };
        var result = await _controller.Propose(request);

        result.Result.Should().NotBeOfType<CreatedResult>();
    }

    [Fact]
    public async Task Propose_NamespaceNotOwned_ReturnsError_DoesNotPropose()
    {
        var unknownId = Guid.NewGuid();
        _namespaceRepository
            .Setup(r => r.GetByIdAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("NS_NOT_FOUND", "not found")));

        var result = await _controller.Propose(ValidRequest(unknownId));

        result.Result.Should().NotBeOfType<CreatedResult>();
        _playbookLedger.Verify(l => l.ProposeAsync(It.IsAny<ProposePlaybookEntryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Propose_InsufficientGovernanceRole_ReturnsForbidden_DoesNotPropose()
    {
        var ns = CreateNamespace();
        SetupOwnedNamespace(ns);
        _governanceAccessEvaluator
            .Setup(e => e.EvaluateAsync(
                OwnerId, It.IsAny<string>(), GovernanceRole.Operator, ns.Id, PillarKind.Prevent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.Forbidden("Governance.InsufficientRole", "denied")));

        var result = await _controller.Propose(ValidRequest(ns.Id));

        result.Result.Should().NotBeOfType<CreatedResult>();
        _playbookLedger.Verify(l => l.ProposeAsync(It.IsAny<ProposePlaybookEntryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Propose_SupersedesEntryNotApproved_ReturnsConflict()
    {
        var ns = CreateNamespace();
        SetupOwnedNamespace(ns);
        var priorEntry = BuildRuleEntry(ns.Id, PlaybookEntryState.Proposed);
        _playbookLedger
            .Setup(l => l.GetEntryAsync(It.IsAny<Guid>(), OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(priorEntry);

        var result = await _controller.Propose(ValidRequest(ns.Id, supersedesRuleEntryId: Guid.NewGuid()));

        result.Result.Should().NotBeOfType<CreatedResult>();
        _playbookLedger.Verify(l => l.ProposeAsync(It.IsAny<ProposePlaybookEntryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Propose_SupersedesApprovedEntry_CarriesForwardLineageAndIncrementsVersion()
    {
        var ns = CreateNamespace();
        SetupOwnedNamespace(ns);
        var lineageId = Guid.NewGuid();
        var priorRule = new PreventionRuleProposal(
            lineageId, 1, "Test rule", "orders-queue",
            new PreventionRuleCondition("SchemaShapeDrift", 70, 1, 24),
            null, DateTimeOffset.UtcNow.AddDays(30), PreventionRuleActions.ObserveOnly);
        var priorEntry = BuildRuleEntry(ns.Id, PlaybookEntryState.Approved, priorRule);
        _playbookLedger
            .Setup(l => l.GetEntryAsync(priorEntry.Id, OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(priorEntry);

        ProposePlaybookEntryRequest? captured = null;
        _playbookLedger
            .Setup(l => l.ProposeAsync(It.IsAny<ProposePlaybookEntryRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProposePlaybookEntryRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(Result<PlaybookEntry>.Success(BuildRuleEntry(ns.Id, PlaybookEntryState.Proposed)));

        var result = await _controller.Propose(ValidRequest(ns.Id, supersedesRuleEntryId: priorEntry.Id));

        result.Result.Should().BeOfType<CreatedResult>();
        var newRule = JsonSerializer.Deserialize<PreventionRuleProposal>(captured!.ProposalJson)!;
        newRule.RuleLineageId.Should().Be(lineageId);
        newRule.RuleVersion.Should().Be(2);
        newRule.SupersedesRuleEntryId.Should().Be(priorEntry.Id);
    }

    // ── Revoke ──────────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_ExistingEntry_CallsLedgerRevokeWithReason()
    {
        var ns = CreateNamespace();
        var entry = BuildRuleEntry(ns.Id);
        _playbookLedger
            .Setup(l => l.GetEntryAsync(entry.Id, OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _playbookLedger
            .Setup(l => l.RevokeAsync(entry.Id, OwnerId, It.IsAny<PlaybookActor>(), "No longer needed.", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PlaybookEntry>.Success(entry));

        var result = await _controller.Revoke(entry.Id, new RevokePreventionRuleRequest("No longer needed."));

        result.Result.Should().BeOfType<OkObjectResult>();
        var response = ((OkObjectResult)result.Result!).Value.Should().BeOfType<PlaybookEntryResponse>().Subject;
        response.Id.Should().Be(entry.Id);
    }

    [Fact]
    public async Task Revoke_EntryIsNotAPreventionRuleProposal_ReturnsNotFound_DoesNotCheckGovernance()
    {
        var ns = CreateNamespace();
        var otherKindEntry = new PlaybookEntry
        {
            OwnerId = OwnerId,
            PillarKind = PillarKind.Recover,
            ProposalKind = "ReplayPlan",
            EvidenceRefJson = "{}",
            ProposalJson = "{}",
            ProposedAt = DateTimeOffset.UtcNow,
            ProposerIdentity = "System:Test",
            ProposerKind = PlaybookActorKind.System,
            NamespaceId = ns.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            State = PlaybookEntryState.Approved,
            Disposition = PlaybookDisposition.Approved,
        };
        _playbookLedger
            .Setup(l => l.GetEntryAsync(otherKindEntry.Id, OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherKindEntry);

        var result = await _controller.Revoke(otherKindEntry.Id, new RevokePreventionRuleRequest("reason"));

        result.Result.Should().BeOfType<NotFoundResult>();
        _governanceAccessEvaluator.Verify(
            e => e.EvaluateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GovernanceRole>(),
                It.IsAny<Guid?>(), It.IsAny<PillarKind?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _playbookLedger.Verify(
            l => l.RevokeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<PlaybookActor>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Revoke_UnknownEntry_ReturnsNotFound()
    {
        _playbookLedger
            .Setup(l => l.GetEntryAsync(It.IsAny<Guid>(), OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlaybookEntry?)null);

        var result = await _controller.Revoke(Guid.NewGuid(), new RevokePreventionRuleRequest("reason"));

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Revoke_InsufficientGovernanceRole_ReturnsForbidden_DoesNotCallLedgerRevoke()
    {
        var ns = CreateNamespace();
        var entry = BuildRuleEntry(ns.Id);
        _playbookLedger
            .Setup(l => l.GetEntryAsync(entry.Id, OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        _governanceAccessEvaluator
            .Setup(e => e.EvaluateAsync(
                OwnerId, It.IsAny<string>(), GovernanceRole.Operator, ns.Id, PillarKind.Prevent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.Forbidden("Governance.InsufficientRole", "denied")));

        var result = await _controller.Revoke(entry.Id, new RevokePreventionRuleRequest("reason"));

        result.Result.Should().NotBeOfType<OkObjectResult>();
        _playbookLedger.Verify(
            l => l.RevokeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<PlaybookActor>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── GetActive ───────────────────────────────────────────────────

    [Fact]
    public async Task GetActive_ReturnsEvaluationServiceResult()
    {
        var ns = CreateNamespace();
        var entry = BuildRuleEntry(ns.Id);
        _evaluationService
            .Setup(s => s.GetActiveRulesAsync(OwnerId, ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { entry });

        var result = await _controller.GetActive(ns.Id);

        var response = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var entries = response.Value.Should().BeAssignableTo<IReadOnlyList<PlaybookEntryResponse>>().Subject;
        entries.Should().ContainSingle(e => e.Id == entry.Id);
    }
}
