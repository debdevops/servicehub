using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Controllers.V1;
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
        _controller = new RecoveryController(_recoveryLedger)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Items = { { "OwnerId", OwnerA } }
                }
            }
        };
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
        var act = () => new RecoveryController(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("recoveryLedger");
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
    public async Task GetOperationById_OwnedOperation_ReturnsIt()
    {
        var operation = await OpenOperationAsync(OwnerA);

        var result = await _controller.GetOperationById(operation.Id);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<RecoveryOperationResponse>().Subject;
        response.Id.Should().Be(operation.Id);
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
}
