using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Api.Security;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public sealed class RecoveryControllerTests : IDisposable
{
    private const string OwnerA = "entra:owner-a";
    private const string OwnerB = "entra:owner-b";

    private readonly DlqDbContext _dbContext;
    private readonly IRecoveryLedger _recoveryLedger;
    private readonly IRecoveryEvidenceExporter _evidenceExporter;
    private readonly IRecoveryTrustScoringService _trustScoring;
    private readonly RecoveryController _controller;

    public RecoveryControllerTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _recoveryLedger = new RecoveryLedgerService(_dbContext);
        _evidenceExporter = new RecoveryEvidenceExporter(_recoveryLedger);
        _trustScoring = new RecoveryTrustScoringService(_recoveryLedger);
        _controller = CreateController(OwnerA);
    }

    private RecoveryController CreateController(string ownerId) => new(_recoveryLedger, _evidenceExporter, _trustScoring)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                Items = { { "OwnerId", ownerId } }
            }
        }
    };

    private static void SetExplicitIntent(RecoveryController controller, string intent)
    {
        controller.HttpContext.Request.Headers[IntentHeaders.IntentHeaderName] = intent;
        controller.HttpContext.Request.Headers[IntentHeaders.ConfirmHeaderName] = "true";
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private async Task<RecoveryOperation> OpenOperationAsync(string ownerId, Guid? namespaceId = null)
    {
        var result = await _recoveryLedger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ownerId,
            Kind = RecoveryOperationKind.Replay,
            Trigger = RecoveryTrigger.Manual,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            NamespaceId = namespaceId,
            ScopeDescription = "test",
            TargetCount = 1,
        });
        return result.Value;
    }

    [Fact]
    public void Constructor_NullRecoveryLedger_Throws()
    {
        var act = () => new RecoveryController(null!, _evidenceExporter, _trustScoring);
        act.Should().Throw<ArgumentNullException>().WithParameterName("recoveryLedger");
    }

    [Fact]
    public void Constructor_NullEvidenceExporter_Throws()
    {
        var act = () => new RecoveryController(_recoveryLedger, null!, _trustScoring);
        act.Should().Throw<ArgumentNullException>().WithParameterName("evidenceExporter");
    }

    [Fact]
    public void Constructor_NullTrustScoring_Throws()
    {
        var act = () => new RecoveryController(_recoveryLedger, _evidenceExporter, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("trustScoring");
    }

    [Fact]
    public async Task GetOperations_ReturnsOnlyCallerOwnedOperations()
    {
        await OpenOperationAsync(OwnerA);
        await OpenOperationAsync(OwnerB);

        var result = await _controller.GetOperations();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var operations = ok.Value.Should().BeAssignableTo<IReadOnlyList<RecoveryOperationResponse>>().Subject;
        operations.Should().ContainSingle();
    }

    [Fact]
    public async Task GetOperations_NamespaceFilter_OnlyReturnsMatchingNamespace()
    {
        var namespaceId = Guid.NewGuid();
        await OpenOperationAsync(OwnerA, namespaceId);
        await OpenOperationAsync(OwnerA, Guid.NewGuid());

        var result = await _controller.GetOperations(namespaceId: namespaceId);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var operations = ok.Value.Should().BeAssignableTo<IReadOnlyList<RecoveryOperationResponse>>().Subject;
        operations.Should().ContainSingle();
        operations[0].NamespaceId.Should().Be(namespaceId);
    }

    [Fact]
    public async Task GetOperationById_DifferentOwner_ReturnsNotFound()
    {
        var operation = await OpenOperationAsync(OwnerB);

        var result = await _controller.GetOperationById(operation.Id);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetOperationById_OwnedOperation_ReturnsDetailWithEntriesAndEvents()
    {
        // Phase 5: GetOperationById now returns the composite detail (operation + entries +
        // events) the operation detail page needs in one round trip, not the bare operation
        // header alone.
        var operation = await OpenOperationAsync(OwnerA);
        await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = OwnerA,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            BodyHash = "hash-detail",
            TargetEntity = "queue-detail",
        });

        var result = await _controller.GetOperationById(operation.Id);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<RecoveryOperationDetailResponse>().Subject;
        response.Operation.Id.Should().Be(operation.Id);
        response.Entries.Should().ContainSingle(e => e.TargetEntity == "queue-detail");
        response.Events.Should().Contain(e => e.EventType == nameof(RecoveryEventType.OperationOpened));
        response.Events.Should().Contain(e => e.EventType == nameof(RecoveryEventType.EntryBegun));
        response.Events.Should().BeInAscendingOrder(e => e.Seq);
    }

    [Fact]
    public async Task GetEntries_ReturnsOnlyCallerOwnedEntries()
    {
        var operationA = await OpenOperationAsync(OwnerA);
        var operationB = await OpenOperationAsync(OwnerB);

        await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operationA.Id,
            OwnerId = OwnerA,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            BodyHash = "hash-a",
            TargetEntity = "queue-a",
        });
        await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operationB.Id,
            OwnerId = OwnerB,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            BodyHash = "hash-b",
            TargetEntity = "queue-b",
        });

        var result = await _controller.GetEntries();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var entries = ok.Value.Should().BeAssignableTo<IReadOnlyList<RecoveryLedgerEntryResponse>>().Subject;
        entries.Should().ContainSingle();
        entries[0].TargetEntity.Should().Be("queue-a");
    }

    [Fact]
    public async Task GetAgeing_ReturnsOnlyNonTerminalEntriesForCaller()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var openEntry = await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = OwnerA,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            BodyHash = "hash-open",
            TargetEntity = "queue-open",
        });
        var closedEntry = await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = OwnerA,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            BodyHash = "hash-closed",
            TargetEntity = "queue-closed",
        });
        await _recoveryLedger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = closedEntry.Value.Id,
            OwnerId = OwnerA,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            Outcome = RecoveryExecutionOutcome.Rejected,
        });

        var result = await _controller.GetAgeing();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var entries = ok.Value.Should().BeAssignableTo<IReadOnlyList<RecoveryLedgerEntryResponse>>().Subject;
        entries.Should().ContainSingle();
        entries[0].Id.Should().Be(openEntry.Value.Id);
    }

    [Fact]
    public async Task Export_DifferentOwner_ReturnsNotFound()
    {
        var operation = await OpenOperationAsync(OwnerB);

        var result = await _controller.Export(operation.Id);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Export_DefaultFormat_ReturnsJsonBundleWithNonEmptyDoesNotKnow()
    {
        var operation = await OpenOperationAsync(OwnerA);

        var result = await _controller.Export(operation.Id);

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("application/json");
        var json = Encoding.UTF8.GetString(file.FileContents);
        json.Should().Contain("whatServiceHubDoesNotKnow");
        json.Should().Contain("tamperEvidentNotTamperProof");
    }

    [Fact]
    public async Task Export_CsvFormat_ReturnsCsvContentType()
    {
        var operation = await OpenOperationAsync(OwnerA);

        var result = await _controller.Export(operation.Id, format: "csv");

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("text/csv");
    }

    [Fact]
    public async Task Export_PackageFormat_ReturnsZip()
    {
        var operation = await OpenOperationAsync(OwnerA);

        var result = await _controller.Export(operation.Id, format: "package");

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("application/zip");
        file.FileContents.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Export_TwoConsecutiveExports_AreByteIdenticalExceptExportedAtAndBy()
    {
        var operation = await OpenOperationAsync(OwnerA);

        var first = await _controller.Export(operation.Id);
        var second = await _controller.Export(operation.Id);

        var firstJson = Encoding.UTF8.GetString(first.Should().BeOfType<FileContentResult>().Subject.FileContents);
        var secondJson = Encoding.UTF8.GetString(second.Should().BeOfType<FileContentResult>().Subject.FileContents);

        // Strip the two fields the export contract explicitly allows to vary (roadmap §16.5).
        var normalizedFirst = System.Text.RegularExpressions.Regex.Replace(
            firstJson, "\"exportedAt\":\\s*\"[^\"]*\"", "\"exportedAt\":\"REDACTED\"");
        var normalizedSecond = System.Text.RegularExpressions.Regex.Replace(
            secondJson, "\"exportedAt\":\\s*\"[^\"]*\"", "\"exportedAt\":\"REDACTED\"");

        normalizedFirst.Should().Be(normalizedSecond);
    }

    [Fact]
    public async Task VerifyChain_DifferentOwner_ReturnsNotFound()
    {
        var operation = await OpenOperationAsync(OwnerB);

        var result = await _controller.VerifyChain(operation.Id);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task VerifyChain_IntactChain_ReturnsValid()
    {
        var operation = await OpenOperationAsync(OwnerA);

        var result = await _controller.VerifyChain(operation.Id);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var chainResult = ok.Value.Should().BeOfType<ChainVerificationResult>().Subject;
        chainResult.IsValid.Should().BeTrue();
        chainResult.OwnerId.Should().Be(OwnerA);
    }

    [Fact]
    public async Task WriteOff_MissingIntentHeaders_ReturnsPreconditionRequired()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var entry = await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = OwnerA,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            BodyHash = "hash-writeoff",
            TargetEntity = "queue-writeoff",
        });

        var result = await _controller.WriteOff(entry.Value.Id, new WriteOffRecoveryEntryRequest("unrecoverable"));

        var problem = result.Result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status428PreconditionRequired);
    }

    [Fact]
    public async Task WriteOff_WithIntent_WritesOffEntry()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var entry = await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = OwnerA,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            BodyHash = "hash-writeoff-2",
            TargetEntity = "queue-writeoff-2",
        });
        SetExplicitIntent(_controller, IntentHeaders.IntentWriteOffRecovery);

        var result = await _controller.WriteOff(entry.Value.Id, new WriteOffRecoveryEntryRequest("operator gave up"));

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<RecoveryLedgerEntryResponse>().Subject;
        response.State.Should().Be(nameof(RecoveryEntryState.WrittenOff));
    }

    [Fact]
    public async Task WriteOff_DifferentOwnersEntry_ReturnsNotFound()
    {
        var operationB = await OpenOperationAsync(OwnerB);
        var entryB = await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operationB.Id,
            OwnerId = OwnerB,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            BodyHash = "hash-cross-owner",
            TargetEntity = "queue-cross-owner",
        });
        SetExplicitIntent(_controller, IntentHeaders.IntentWriteOffRecovery);

        // _controller is authenticated as OwnerA attempting to write off OwnerB's entry.
        var result = await _controller.WriteOff(entryB.Value.Id, new WriteOffRecoveryEntryRequest("not mine"));

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── Emergency Stop endpoints (§9.4.2, §15.2) ─────────────────────────────

    [Fact]
    public void ActivateAndClearEmergencyStop_RequireAdminScope()
    {
        // Pins the specific scope, not merely "some scope" — ScopeConformanceTests already
        // guards that every action declares [RequireScope] at all; this guards the value.
        typeof(RecoveryController)
            .GetMethod(nameof(RecoveryController.ActivateEmergencyStop))!
            .GetCustomAttributes(typeof(RequireScopeAttribute), inherit: true)
            .Cast<RequireScopeAttribute>()
            .Single().Scope.Should().Be(ApiKeyScopes.Admin);

        typeof(RecoveryController)
            .GetMethod(nameof(RecoveryController.ClearEmergencyStop))!
            .GetCustomAttributes(typeof(RequireScopeAttribute), inherit: true)
            .Cast<RequireScopeAttribute>()
            .Single().Scope.Should().Be(ApiKeyScopes.Admin);
    }

    [Fact]
    public async Task GetEmergencyStopStatus_InitiallyInactive_ReturnsFalse()
    {
        var result = await _controller.GetEmergencyStopStatus();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<EmergencyStopStatusResponse>()
            .Subject.Active.Should().BeFalse();
    }

    [Fact]
    public async Task ActivateEmergencyStop_ReturnsActiveTrueAndPersists()
    {
        var result = await _controller.ActivateEmergencyStop(new EmergencyStopRequest("incident 42"));

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<EmergencyStopStatusResponse>().Subject.Active.Should().BeTrue();

        (await _recoveryLedger.IsEmergencyStopActiveAsync(OwnerA)).Should().BeTrue();
    }

    [Fact]
    public async Task ActivateEmergencyStop_NullRequestBody_Succeeds()
    {
        // Reason is optional — an admin can activate without a request body at all.
        var result = await _controller.ActivateEmergencyStop(request: null);

        result.Result.Should().BeOfType<OkObjectResult>();
        (await _recoveryLedger.IsEmergencyStopActiveAsync(OwnerA)).Should().BeTrue();
    }

    [Fact]
    public async Task ClearEmergencyStop_AfterActivation_ReturnsActiveFalseAndPersists()
    {
        await _controller.ActivateEmergencyStop(new EmergencyStopRequest("incident 43"));

        var result = await _controller.ClearEmergencyStop(new EmergencyStopRequest("resolved"));

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<EmergencyStopStatusResponse>().Subject.Active.Should().BeFalse();

        (await _recoveryLedger.IsEmergencyStopActiveAsync(OwnerA)).Should().BeFalse();
    }

    [Fact]
    public async Task ActivateEmergencyStop_DoesNotAffectAnotherOwner()
    {
        var controllerB = CreateController(OwnerB);

        await _controller.ActivateEmergencyStop(new EmergencyStopRequest(null));

        (await _recoveryLedger.IsEmergencyStopActiveAsync(OwnerA)).Should().BeTrue();
        (await _recoveryLedger.IsEmergencyStopActiveAsync(OwnerB)).Should().BeFalse();

        var statusB = await controllerB.GetEmergencyStopStatus();
        var okB = statusB.Result.Should().BeOfType<OkObjectResult>().Subject;
        okB.Value.Should().BeOfType<EmergencyStopStatusResponse>().Subject.Active.Should().BeFalse();
    }

    [Fact]
    public async Task ActivateEmergencyStop_CalledTwice_RemainsActiveAndAppendsBothAsForensicEvidence()
    {
        await _controller.ActivateEmergencyStop(new EmergencyStopRequest("first alert"));
        var second = await _controller.ActivateEmergencyStop(new EmergencyStopRequest("second alert"));

        second.Result.Should().BeOfType<OkObjectResult>();
        (await _recoveryLedger.IsEmergencyStopActiveAsync(OwnerA)).Should().BeTrue();

        var activatedEvents = await _dbContext.RecoveryEvents
            .Where(e => e.OwnerId == OwnerA && e.EventType == RecoveryEventType.EmergencyStopActivated)
            .ToListAsync();
        activatedEvents.Should().HaveCount(2, "each activation is its own forensic fact, never suppressed as a duplicate");
    }

    // ── FlagOutcome (roadmap §8.10, §9.3) ─────────────────────────────────────

    [Fact]
    public void FlagOutcome_RequiresRecoveryWriteScope()
    {
        typeof(RecoveryController)
            .GetMethod(nameof(RecoveryController.FlagOutcome))!
            .GetCustomAttributes(typeof(RequireScopeAttribute), inherit: true)
            .Cast<RequireScopeAttribute>()
            .Single().Scope.Should().Be(ApiKeyScopes.RecoveryWrite);
    }

    [Fact]
    public async Task FlagOutcome_WritesOutcomeFlaggedEvent()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var entry = await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = OwnerA,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            BodyHash = "hash-flag",
            TargetEntity = "queue-flag",
        });

        var result = await _controller.FlagOutcome(
            entry.Value.Id, new FlagRecoveryOutcomeRequest(RecoveryOutcomeFlagKind.Unsafe, "customer reported data loss"));

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<RecoveryEventResponse>().Subject;
        response.EventType.Should().Be(nameof(RecoveryEventType.OutcomeFlagged));
        response.EntryId.Should().Be(entry.Value.Id);
        response.DetailJson.Should().Contain("Unsafe").And.Contain("customer reported data loss");
    }

    [Fact]
    public async Task FlagOutcome_DifferentOwnersEntry_ReturnsNotFound()
    {
        var operationB = await OpenOperationAsync(OwnerB);
        var entryB = await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operationB.Id,
            OwnerId = OwnerB,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            BodyHash = "hash-flag-cross-owner",
            TargetEntity = "queue-flag-cross-owner",
        });

        // _controller is authenticated as OwnerA attempting to flag OwnerB's entry.
        var result = await _controller.FlagOutcome(
            entryB.Value.Id, new FlagRecoveryOutcomeRequest(RecoveryOutcomeFlagKind.Unsafe, "not mine"));

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetTrustEvidence_AfterUnsafeFlag_ReportsDisqualifierPresent()
    {
        var operation = await OpenOperationAsync(OwnerA);
        var entry = await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = OwnerA,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            BodyHash = "hash-trust-flag",
            SignatureHashSnapshot = "sig-trust-flag",
            TargetEntity = "queue-trust-flag",
        });
        await _controller.FlagOutcome(
            entry.Value.Id, new FlagRecoveryOutcomeRequest(RecoveryOutcomeFlagKind.Unsafe, "incident"));

        var result = await _controller.GetTrustEvidence("sig-trust-flag");

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<SignatureTrustEvidenceResponse>().Subject;
        response.UnsafeOutcomePresent.Should().BeTrue();
    }
}
