using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public class RulesControllerTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly Mock<IRuleEngine> _ruleEngine = new();
    private readonly Mock<ILogger<RulesController>> _logger = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly RulesController _controller;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public RulesControllerTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _controller = new RulesController(_dbContext, _ruleEngine.Object, _logger.Object, _auditLogger.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                Items = { { "OwnerId", TestConstants.TestOwnerId } }
            }
        };

        // Provide a valid ApiKeyConfig so in-method scope checks pass
        _controller.ControllerContext.HttpContext.Items["ApiKeyConfig"] = new ApiKeyConfiguration
        {
            Key = "test-key-12345678",
            Scopes = null  // null = admin (all scopes granted)
        };
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private AutoReplayRule CreateRule(string name = "Test Rule", bool enabled = true)
    {
        var conditions = new List<RuleCondition>
        {
            new() { Field = "FailureCategory", Operator = "Equals", Value = "Transient" }
        };
        var action = new RuleAction { AutoReplay = true, DelaySeconds = 60, MaxRetries = 3 };

        return new AutoReplayRule
        {
            Name = name,
            OwnerId = TestConstants.TestOwnerId,
            Description = "A test rule",
            Enabled = enabled,
            ConditionsJson = JsonSerializer.Serialize(conditions, JsonOptions),
            ActionsJson = JsonSerializer.Serialize(action, JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow,
            MaxReplaysPerHour = 100
        };
    }

    private DlqMessage CreateDlqMessage(long seq = 1, DlqMessageStatus status = DlqMessageStatus.Active)
    {
        return new DlqMessage
        {
            MessageId = $"msg-{seq}",
            SequenceNumber = seq,
            BodyHash = $"hash-{seq}",
            NamespaceId = Guid.NewGuid(),
            OwnerId = TestConstants.TestOwnerId,
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddHours(-1),
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeadLetterReason = "MaxDeliveryCountExceeded",
            DeliveryCount = 10,
            Status = status,
            FailureCategory = FailureCategory.Transient
        };
    }

    private CreateRuleRequest CreateRuleRequest(string name = "New Rule")
    {
        return new CreateRuleRequest
        {
            Name = name,
            Description = "New test rule",
            Enabled = true,
            Conditions = new List<RuleCondition>
            {
                new() { Field = "FailureCategory", Operator = "Equals", Value = "Transient" }
            },
            Action = new RuleAction { AutoReplay = true, DelaySeconds = 60, MaxRetries = 3 },
            MaxReplaysPerHour = 100
        };
    }

    // ── Constructor ─────────────────────────────────────────

    [Fact]
    public void Constructor_NullDbContext_Throws()
    {
        var act = () => new RulesController(null!, _ruleEngine.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
    }

    [Fact]
    public void Constructor_NullRuleEngine_Throws()
    {
        var act = () => new RulesController(_dbContext, null!, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("ruleEngine");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new RulesController(_dbContext, _ruleEngine.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── GetAll ──────────────────────────────────────────────

    [Fact]
    public async Task GetAll_Empty_ReturnsEmptyList()
    {
        var result = await _controller.GetAll();
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value.Should().BeAssignableTo<IEnumerable<RuleResponse>>().Subject;
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAll_WithRules_ReturnsAll()
    {
        _dbContext.AutoReplayRules.Add(CreateRule("Rule 1"));
        _dbContext.AutoReplayRules.Add(CreateRule("Rule 2"));
        await _dbContext.SaveChangesAsync();

        _ruleEngine.Setup(r => r.Evaluate(It.IsAny<DlqMessage>(), It.IsAny<IReadOnlyList<RuleCondition>>()))
            .Returns(new RuleMatchResult
            {
                MessageId = 1, ServiceBusMessageId = "msg-1", EntityName = "q",
                IsMatch = false
            });

        var result = await _controller.GetAll();
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value.Should().BeAssignableTo<IEnumerable<RuleResponse>>().Subject.ToList();
        list.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAll_EnabledOnlyFilter_ReturnsOnlyEnabled()
    {
        _dbContext.AutoReplayRules.Add(CreateRule("Enabled", true));
        _dbContext.AutoReplayRules.Add(CreateRule("Disabled", false));
        await _dbContext.SaveChangesAsync();

        _ruleEngine.Setup(r => r.Evaluate(It.IsAny<DlqMessage>(), It.IsAny<IReadOnlyList<RuleCondition>>()))
            .Returns(new RuleMatchResult
            {
                MessageId = 1, ServiceBusMessageId = "msg-1", EntityName = "q",
                IsMatch = false
            });

        var result = await _controller.GetAll(enabledOnly: true);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value.Should().BeAssignableTo<IEnumerable<RuleResponse>>().Subject.ToList();
        list.Should().HaveCount(1);
        list[0].Name.Should().Be("Enabled");
    }

    [Fact]
    public async Task GetAll_WithActiveMessages_ComputesPendingCount()
    {
        var rule = CreateRule("Match Rule");
        _dbContext.AutoReplayRules.Add(rule);
        _dbContext.DlqMessages.Add(CreateDlqMessage(1));
        _dbContext.DlqMessages.Add(CreateDlqMessage(2));
        await _dbContext.SaveChangesAsync();

        _ruleEngine.Setup(r => r.Evaluate(It.IsAny<DlqMessage>(), It.IsAny<IReadOnlyList<RuleCondition>>()))
            .Returns(new RuleMatchResult
            {
                MessageId = 1, ServiceBusMessageId = "msg-1", EntityName = "q",
                IsMatch = true, MatchReason = "Matched"
            });

        var result = await _controller.GetAll();
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value.Should().BeAssignableTo<IEnumerable<RuleResponse>>().Subject.ToList();
        list[0].PendingMatchCount.Should().Be(2);
    }

    // ── GetById ─────────────────────────────────────────────

    [Fact]
    public async Task GetById_Exists_ReturnsRule()
    {
        var rule = CreateRule();
        _dbContext.AutoReplayRules.Add(rule);
        await _dbContext.SaveChangesAsync();

        _ruleEngine.Setup(r => r.Evaluate(It.IsAny<DlqMessage>(), It.IsAny<IReadOnlyList<RuleCondition>>()))
            .Returns(new RuleMatchResult
            {
                MessageId = 1, ServiceBusMessageId = "msg-1", EntityName = "q",
                IsMatch = false
            });

        var result = await _controller.GetById(rule.Id);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<RuleResponse>().Subject;
        response.Name.Should().Be("Test Rule");
    }

    [Fact]
    public async Task GetById_NotFound_ReturnsError()
    {
        var result = await _controller.GetById(999);
        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    // ── Create ──────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidRequest_ReturnsCreated()
    {
        var request = CreateRuleRequest();
        var result = await _controller.Create(request);
        result.Result.Should().BeOfType<CreatedAtActionResult>();
        _auditLogger.Verify(a => a.LogCriticalAction(
            It.IsAny<HttpContext>(), It.IsAny<string>(), "Rule.Create", "Succeeded",
            It.IsAny<Guid?>(), It.IsAny<EnvironmentType?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), request.Name, It.IsAny<long?>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        _dbContext.AutoReplayRules.Add(CreateRule("Existing Rule"));
        await _dbContext.SaveChangesAsync();

        var request = CreateRuleRequest("Existing Rule");
        var result = await _controller.Create(request);
        result.Result.Should().NotBeOfType<CreatedAtActionResult>();
        _auditLogger.Verify(a => a.LogCriticalAction(
            It.IsAny<HttpContext>(), It.IsAny<string>(), "Rule.Create", It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<EnvironmentType?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<string?>()), Times.Never);
    }

    // ── Update ──────────────────────────────────────────────

    [Fact]
    public async Task Update_Exists_ReturnsUpdated()
    {
        var rule = CreateRule();
        _dbContext.AutoReplayRules.Add(rule);
        await _dbContext.SaveChangesAsync();
        var id = rule.Id;

        var request = CreateRuleRequest("Updated Rule");
        var result = await _controller.Update(id, request);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<RuleResponse>().Subject;
        response.Name.Should().Be("Updated Rule");
        _auditLogger.Verify(a => a.LogCriticalAction(
            It.IsAny<HttpContext>(), It.IsAny<string>(), "Rule.Update", "Succeeded",
            It.IsAny<Guid?>(), It.IsAny<EnvironmentType?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), "Updated Rule", It.IsAny<long?>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task Update_NotFound_ReturnsError()
    {
        var request = CreateRuleRequest();
        var result = await _controller.Update(999, request);
        result.Result.Should().NotBeOfType<OkObjectResult>();
        _auditLogger.Verify(a => a.LogCriticalAction(
            It.IsAny<HttpContext>(), It.IsAny<string>(), "Rule.Update", It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<EnvironmentType?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Update_DuplicateName_ReturnsConflict()
    {
        _dbContext.AutoReplayRules.Add(CreateRule("Rule A"));
        _dbContext.AutoReplayRules.Add(CreateRule("Rule B"));
        await _dbContext.SaveChangesAsync();
        var ruleB = await _dbContext.AutoReplayRules.FirstAsync(r => r.Name == "Rule B");

        var request = CreateRuleRequest("Rule A");
        var result = await _controller.Update(ruleB.Id, request);
        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    // ── Delete ──────────────────────────────────────────────

    [Fact]
    public async Task Delete_Exists_ReturnsNoContent()
    {
        var rule = CreateRule();
        _dbContext.AutoReplayRules.Add(rule);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.Delete(rule.Id);
        result.Should().BeOfType<NoContentResult>();
        _auditLogger.Verify(a => a.LogCriticalAction(
            It.IsAny<HttpContext>(), It.IsAny<string>(), "Rule.Delete", "Succeeded",
            It.IsAny<Guid?>(), It.IsAny<EnvironmentType?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), rule.Name, It.IsAny<long?>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task Delete_NotFound_ReturnsError()
    {
        var result = await _controller.Delete(999);
        result.Should().NotBeOfType<NoContentResult>();
        _auditLogger.Verify(a => a.LogCriticalAction(
            It.IsAny<HttpContext>(), It.IsAny<string>(), "Rule.Delete", It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<EnvironmentType?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<string?>()), Times.Never);
    }

    // ── Toggle ──────────────────────────────────────────────

    [Fact]
    public async Task Toggle_EnabledRule_BecomesDisabled()
    {
        var rule = CreateRule(enabled: true);
        _dbContext.AutoReplayRules.Add(rule);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.Toggle(rule.Id);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<RuleResponse>().Subject;
        response.Enabled.Should().BeFalse();
        _auditLogger.Verify(a => a.LogCriticalAction(
            It.IsAny<HttpContext>(), It.IsAny<string>(), "Rule.Toggle", "Succeeded",
            It.IsAny<Guid?>(), It.IsAny<EnvironmentType?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), rule.Name, It.IsAny<long?>(), "disabled"), Times.Once);
    }

    [Fact]
    public async Task Toggle_DisabledRule_BecomesEnabled()
    {
        var rule = CreateRule(enabled: false);
        _dbContext.AutoReplayRules.Add(rule);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.Toggle(rule.Id);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<RuleResponse>().Subject;
        response.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Toggle_NotFound_ReturnsError()
    {
        var result = await _controller.Toggle(999);
        result.Result.Should().NotBeOfType<OkObjectResult>();
        _auditLogger.Verify(a => a.LogCriticalAction(
            It.IsAny<HttpContext>(), It.IsAny<string>(), "Rule.Toggle", It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<EnvironmentType?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<string?>()), Times.Never);
    }

    // ── TestRule ─────────────────────────────────────────────

    [Fact]
    public async Task TestRule_WithConditions_ReturnsResults()
    {
        _dbContext.DlqMessages.Add(CreateDlqMessage(1));
        _dbContext.DlqMessages.Add(CreateDlqMessage(2));
        await _dbContext.SaveChangesAsync();

        var matchResults = new List<RuleMatchResult>
        {
            new() { MessageId = 1, ServiceBusMessageId = "msg-1", EntityName = "test-queue", IsMatch = true, MatchReason = "Matched" },
            new() { MessageId = 2, ServiceBusMessageId = "msg-2", EntityName = "test-queue", IsMatch = false }
        };

        _ruleEngine.Setup(r => r.EvaluateBatch(It.IsAny<IReadOnlyList<DlqMessage>>(), It.IsAny<IReadOnlyList<RuleCondition>>()))
            .Returns(matchResults);

        var request = new TestRuleRequest
        {
            Conditions = new List<RuleCondition>
            {
                new() { Field = "FailureCategory", Operator = "Equals", Value = "Transient" }
            }
        };

        var result = await _controller.TestRule(request);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<RuleTestResponse>().Subject;
        response.TotalTested.Should().Be(2);
        response.MatchedCount.Should().Be(1);
    }

    [Fact]
    public async Task TestRule_WithRuleId_UsesExistingRuleConditions()
    {
        var rule = CreateRule();
        _dbContext.AutoReplayRules.Add(rule);
        _dbContext.DlqMessages.Add(CreateDlqMessage(1));
        await _dbContext.SaveChangesAsync();

        _ruleEngine.Setup(r => r.EvaluateBatch(It.IsAny<IReadOnlyList<DlqMessage>>(), It.IsAny<IReadOnlyList<RuleCondition>>()))
            .Returns(new List<RuleMatchResult>());

        var request = new TestRuleRequest { RuleId = rule.Id };
        var result = await _controller.TestRule(request);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<RuleTestResponse>();
    }

    [Fact]
    public async Task TestRule_NoConditionsAndNoRuleId_ReturnsError()
    {
        var request = new TestRuleRequest();
        var result = await _controller.TestRule(request);
        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task TestRule_InvalidRuleId_ReturnsNotFound()
    {
        var request = new TestRuleRequest { RuleId = 999 };
        var result = await _controller.TestRule(request);
        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    // ── GetTemplates ────────────────────────────────────────

    [Fact]
    public async Task GetTemplates_ReturnsBuiltInTemplates()
    {
        var result = _controller.GetTemplates();
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var templates = ok.Value.Should().BeAssignableTo<IEnumerable<RuleTemplateResponse>>().Subject.ToList();
        templates.Should().NotBeEmpty();
        templates.Should().Contain(t => t.Id == "database-timeouts");
        templates.Should().Contain(t => t.Id == "max-delivery-exceeded");
        templates.Should().Contain(t => t.Id == "transient-network-errors");
    }

    // ── GenerateRules ──────────────────────────────────────

    [Fact]
    public async Task GenerateRules_NoActiveMessages_ReturnsEmptySummary()
    {
        var result = await _controller.GenerateRules();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<GenerateRulesResponse>().Subject;

        response.AnalysedMessages.Should().Be(0);
        response.PatternsDetected.Should().Be(0);
        response.RulesCreated.Should().Be(0);
        response.RulesSkipped.Should().Be(0);
        response.Rules.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateRules_WithMatchingPatterns_CreatesRulesWithPersistedIds()
    {
        var namespaceId = Guid.NewGuid();
        var reason = "Timeout while calling downstream payment gateway";

        _dbContext.DlqMessages.AddRange(
            new DlqMessage
            {
                MessageId = "msg-101",
                SequenceNumber = 101,
                BodyHash = "hash-101",
                NamespaceId = namespaceId,
                OwnerId = TestConstants.TestOwnerId,
                EntityName = "orders",
                EntityType = ServiceBusEntityType.Queue,
                EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                DetectedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-9),
                DeadLetterReason = reason,
                DeliveryCount = 3,
                Status = DlqMessageStatus.Active,
                FailureCategory = FailureCategory.Transient,
            },
            new DlqMessage
            {
                MessageId = "msg-102",
                SequenceNumber = 102,
                BodyHash = "hash-102",
                NamespaceId = namespaceId,
                OwnerId = TestConstants.TestOwnerId,
                EntityName = "orders",
                EntityType = ServiceBusEntityType.Queue,
                EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddMinutes(-8),
                DetectedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-7),
                DeadLetterReason = reason,
                DeliveryCount = 4,
                Status = DlqMessageStatus.Active,
                FailureCategory = FailureCategory.Transient,
            },
            new DlqMessage
            {
                MessageId = "msg-103",
                SequenceNumber = 103,
                BodyHash = "hash-103",
                NamespaceId = namespaceId,
                OwnerId = TestConstants.TestOwnerId,
                EntityName = "orders",
                EntityType = ServiceBusEntityType.Queue,
                EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddMinutes(-6),
                DetectedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                DeadLetterReason = reason,
                DeliveryCount = 5,
                Status = DlqMessageStatus.Active,
                FailureCategory = FailureCategory.Transient,
            });

        await _dbContext.SaveChangesAsync();

        _ruleEngine
            .Setup(r => r.Evaluate(It.IsAny<DlqMessage>(), It.IsAny<IReadOnlyList<RuleCondition>>()))
            .Returns(new RuleMatchResult
            {
                MessageId = 1,
                ServiceBusMessageId = "msg",
                EntityName = "orders",
                IsMatch = true,
            });

        var result = await _controller.GenerateRules(namespaceId);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<GenerateRulesResponse>().Subject;

        response.AnalysedMessages.Should().Be(3);
        response.PatternsDetected.Should().BeGreaterThan(0);
        response.RulesCreated.Should().BeGreaterThan(0);
        response.Rules.Should().NotBeEmpty();
        response.Rules.Should().OnlyContain(r => r.Id > 0);
    }

    [Fact]
    public async Task GenerateRules_WhenPatternsAlreadyExist_SkipsDuplicates()
    {
        var namespaceId = Guid.NewGuid();
        const string reason = "Quota exceeded on downstream API";

        _dbContext.DlqMessages.AddRange(
            new DlqMessage
            {
                MessageId = "msg-201",
                SequenceNumber = 201,
                BodyHash = "hash-201",
                NamespaceId = namespaceId,
                OwnerId = TestConstants.TestOwnerId,
                EntityName = "billing",
                EntityType = ServiceBusEntityType.Queue,
                EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                DetectedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-9),
                DeadLetterReason = reason,
                DeliveryCount = 2,
                Status = DlqMessageStatus.Active,
                FailureCategory = FailureCategory.Transient,
            },
            new DlqMessage
            {
                MessageId = "msg-202",
                SequenceNumber = 202,
                BodyHash = "hash-202",
                NamespaceId = namespaceId,
                OwnerId = TestConstants.TestOwnerId,
                EntityName = "billing",
                EntityType = ServiceBusEntityType.Queue,
                EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddMinutes(-8),
                DetectedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-7),
                DeadLetterReason = reason,
                DeliveryCount = 2,
                Status = DlqMessageStatus.Active,
                FailureCategory = FailureCategory.Transient,
            });

        var reasonConditionsJson = JsonSerializer.Serialize(
            new List<RuleCondition>
            {
                new() { Field = "DeadLetterReason", Operator = "Contains", Value = reason }
            },
            JsonOptions);

        var categoryConditionsJson = JsonSerializer.Serialize(
            new List<RuleCondition>
            {
                new() { Field = "FailureCategory", Operator = "Equals", Value = FailureCategory.Transient.ToString() }
            },
            JsonOptions);

        _dbContext.AutoReplayRules.AddRange(
            new AutoReplayRule
            {
                Name = "Existing reason rule",
                OwnerId = TestConstants.TestOwnerId,
            Description = "existing",
                Enabled = true,
                ConditionsJson = reasonConditionsJson,
                ActionsJson = JsonSerializer.Serialize(new RuleAction { AutoReplay = true }, JsonOptions),
                CreatedAt = DateTimeOffset.UtcNow,
                MaxReplaysPerHour = 50,
            },
            new AutoReplayRule
            {
                Name = "Existing category rule",
                OwnerId = TestConstants.TestOwnerId,
            Description = "existing",
                Enabled = true,
                ConditionsJson = categoryConditionsJson,
                ActionsJson = JsonSerializer.Serialize(new RuleAction { AutoReplay = true }, JsonOptions),
                CreatedAt = DateTimeOffset.UtcNow,
                MaxReplaysPerHour = 50,
            });

        await _dbContext.SaveChangesAsync();

        var result = await _controller.GenerateRules(namespaceId);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<GenerateRulesResponse>().Subject;

        response.AnalysedMessages.Should().Be(2);
        response.RulesCreated.Should().Be(0);
        response.RulesSkipped.Should().BeGreaterThanOrEqualTo(2);
    }

    // ── ReplayAll ───────────────────────────────────────────
    //
    // Previously untested: ReplayAll routed through IServiceBusClientCache directly (Azure-only)
    // and never claimed a message before replaying it (roadmap P6). These tests exercise the
    // Phase 3 rewrite onto IMessageOperationsService with the same claim protocol every other
    // replay path uses.

    private static Namespace CreateNamespace(
        string name = "replay-all-ns",
        CloudProviderType provider = CloudProviderType.Azure,
        EnvironmentType environment = EnvironmentType.Dev) =>
        Namespace.Create(
            name,
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            environment: environment,
            provider: provider,
            ownerId: TestConstants.TestOwnerId).Value;

    private void SetIntentHeaders(string intent)
    {
        _controller.ControllerContext.HttpContext.Request.Headers[IntentHeaders.IntentHeaderName] = intent;
        _controller.ControllerContext.HttpContext.Request.Headers[IntentHeaders.ConfirmHeaderName] = "true";
    }

    /// <summary>
    /// Wires <c>HttpContext.RequestServices</c> with everything <c>ReplayAll</c> resolves via
    /// the service locator: the namespace repository, the message operations facade, the
    /// recovery ledger (a real <see cref="RecoveryLedgerService"/> against <see cref="_dbContext"/>
    /// so entries are actually queryable afterward), and the rate-limit executor.
    /// </summary>
    private void SetUpReplayAllServices(
        Namespace ns, Mock<IMessageOperationsService> messageOperations, bool rateLimited = false)
    {
        var namespaceRepository = new Mock<INamespaceRepository>();
        namespaceRepository
            .Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var autoReplayExecutor = new Mock<IAutoReplayExecutor>();
        autoReplayExecutor
            .Setup(e => e.CanReplayAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(!rateLimited);

        var ledger = new RecoveryLedgerService(_dbContext);

        var services = new ServiceCollection();
        services.AddSingleton(namespaceRepository.Object);
        services.AddSingleton(messageOperations.Object);
        services.AddSingleton<IRecoveryLedger>(ledger);
        services.AddSingleton<IRecoveryEligibilityGate>(
            new RecoveryEligibilityGate(ledger, NullLogger<RecoveryEligibilityGate>.Instance));
        services.AddSingleton(autoReplayExecutor.Object);
        _controller.ControllerContext.HttpContext.RequestServices = services.BuildServiceProvider();
    }

    [Fact]
    public async Task ReplayAll_Success_RoutesThroughMessageOperationsServiceAndRecordsLedgerEntry()
    {
        var ns = CreateNamespace();
        var rule = CreateRule();
        _dbContext.AutoReplayRules.Add(rule);
        var message = CreateDlqMessage(1);
        message = new DlqMessage
        {
            MessageId = message.MessageId, SequenceNumber = message.SequenceNumber, BodyHash = message.BodyHash,
            NamespaceId = ns.Id, OwnerId = TestConstants.TestOwnerId, EntityName = message.EntityName,
            EntityType = message.EntityType, EnqueuedTimeUtc = message.EnqueuedTimeUtc,
            DetectedAtUtc = message.DetectedAtUtc, DeadLetterReason = message.DeadLetterReason,
            DeliveryCount = message.DeliveryCount, Status = message.Status, FailureCategory = message.FailureCategory,
        };
        _dbContext.DlqMessages.Add(message);
        await _dbContext.SaveChangesAsync();

        _ruleEngine.Setup(r => r.Evaluate(It.IsAny<DlqMessage>(), It.IsAny<IReadOnlyList<RuleCondition>>()))
            .Returns(new RuleMatchResult { MessageId = message.Id, ServiceBusMessageId = message.MessageId, EntityName = message.EntityName, IsMatch = true });

        var messageOperations = new Mock<IMessageOperationsService>();
        messageOperations
            .Setup(m => m.ReplayMessageAsync(ns.Id, "test-queue", null, message.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        SetUpReplayAllServices(ns, messageOperations);
        SetIntentHeaders(IntentHeaders.IntentReplayAllRules);

        var result = await _controller.ReplayAll(rule.Id);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ReplayAllResponse>().Subject;
        response.Replayed.Should().Be(1);
        response.Failed.Should().Be(0);

        // Never resolved via the service locator — proves the Azure-only client-cache path is gone.
        messageOperations.Verify(
            m => m.ReplayMessageAsync(ns.Id, "test-queue", null, message.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        var reloaded = await _dbContext.DlqMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
        reloaded.Status.Should().Be(DlqMessageStatus.Replayed);

        var entries = await _dbContext.RecoveryLedgerEntries.AsNoTracking().Where(e => e.DlqMessageId == message.Id).ToListAsync();
        entries.Should().ContainSingle();
        entries[0].State.Should().Be(RecoveryEntryState.Observing);
    }

    [Theory]
    [InlineData(CloudProviderType.Gcp)]
    [InlineData(CloudProviderType.Aws)]
    public async Task ReplayAll_NonAzureNamespace_RoutesThroughMessageOperationsService(CloudProviderType provider)
    {
        // Previously impossible to test at all: ReplayAll resolved IServiceBusClientCache
        // directly, which has no meaning for a GCP/AWS namespace (roadmap P6). Routing through
        // IMessageOperationsService — which itself dispatches by provider — means RulesController
        // no longer needs, or has, any Azure-specific dependency on this path.
        var ns = CreateNamespace(provider: provider);
        var rule = CreateRule();
        _dbContext.AutoReplayRules.Add(rule);
        var message = new DlqMessage
        {
            MessageId = "msg-1", SequenceNumber = 1, BodyHash = "hash-1",
            NamespaceId = ns.Id, OwnerId = TestConstants.TestOwnerId, EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue, EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddHours(-1),
            DetectedAtUtc = DateTimeOffset.UtcNow, Status = DlqMessageStatus.Active, CloudProvider = provider,
        };
        _dbContext.DlqMessages.Add(message);
        await _dbContext.SaveChangesAsync();

        _ruleEngine.Setup(r => r.Evaluate(It.IsAny<DlqMessage>(), It.IsAny<IReadOnlyList<RuleCondition>>()))
            .Returns(new RuleMatchResult { MessageId = message.Id, ServiceBusMessageId = message.MessageId, EntityName = message.EntityName, IsMatch = true });

        var messageOperations = new Mock<IMessageOperationsService>();
        messageOperations
            .Setup(m => m.ReplayMessageAsync(ns.Id, "test-queue", null, message.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        SetUpReplayAllServices(ns, messageOperations);
        SetIntentHeaders(IntentHeaders.IntentReplayAllRules);

        var result = await _controller.ReplayAll(rule.Id);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ReplayAllResponse>().Subject;
        response.Replayed.Should().Be(1);

        messageOperations.Verify(
            m => m.ReplayMessageAsync(ns.Id, "test-queue", null, message.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReplayAll_ProviderRejects_RecordsFailureAndLedgerRejection()
    {
        var ns = CreateNamespace();
        var rule = CreateRule();
        _dbContext.AutoReplayRules.Add(rule);
        var message = new DlqMessage
        {
            MessageId = "msg-1", SequenceNumber = 1, BodyHash = "hash-1",
            NamespaceId = ns.Id, OwnerId = TestConstants.TestOwnerId, EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue, EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddHours(-1),
            DetectedAtUtc = DateTimeOffset.UtcNow, Status = DlqMessageStatus.Active,
        };
        _dbContext.DlqMessages.Add(message);
        await _dbContext.SaveChangesAsync();

        _ruleEngine.Setup(r => r.Evaluate(It.IsAny<DlqMessage>(), It.IsAny<IReadOnlyList<RuleCondition>>()))
            .Returns(new RuleMatchResult { MessageId = message.Id, ServiceBusMessageId = message.MessageId, EntityName = message.EntityName, IsMatch = true });

        var messageOperations = new Mock<IMessageOperationsService>();
        messageOperations
            .Setup(m => m.ReplayMessageAsync(ns.Id, "test-queue", null, message.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(Error.ExternalService("Provider.Rejected", "provider declined")));

        SetUpReplayAllServices(ns, messageOperations);
        SetIntentHeaders(IntentHeaders.IntentReplayAllRules);

        var result = await _controller.ReplayAll(rule.Id);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ReplayAllResponse>().Subject;
        response.Failed.Should().Be(1);

        var reloaded = await _dbContext.DlqMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
        reloaded.Status.Should().Be(DlqMessageStatus.ReplayFailed);

        var entries = await _dbContext.RecoveryLedgerEntries.AsNoTracking().Where(e => e.DlqMessageId == message.Id).ToListAsync();
        entries.Should().ContainSingle();
        entries[0].State.Should().Be(RecoveryEntryState.ExecutionFailed);
    }

    [Fact]
    public async Task ReplayAll_MessageClaimedByConcurrentReplay_SkipsWithoutCallingProvider()
    {
        var ns = CreateNamespace();
        var rule = CreateRule();
        _dbContext.AutoReplayRules.Add(rule);

        // ReplayAll snapshots its eligible-message list up front (OrderBy DetectedAtUtc), then
        // processes it sequentially — so `target`, ordered after `decoy`, can go stale before
        // this request reaches it. Same TOCTOU shape as
        // BulkOperationExecutorTests.ExecuteAsync_ConcurrentReplayFromAnotherWorker.
        var decoy = new DlqMessage
        {
            MessageId = "msg-decoy", SequenceNumber = 1, BodyHash = "hash-decoy",
            NamespaceId = ns.Id, OwnerId = TestConstants.TestOwnerId, EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue, EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            DetectedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1), Status = DlqMessageStatus.Active,
        };
        var target = new DlqMessage
        {
            MessageId = "msg-target", SequenceNumber = 2, BodyHash = "hash-target",
            NamespaceId = ns.Id, OwnerId = TestConstants.TestOwnerId, EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue, EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow, Status = DlqMessageStatus.Active,
        };
        _dbContext.DlqMessages.AddRange(decoy, target);
        await _dbContext.SaveChangesAsync();

        _ruleEngine.Setup(r => r.Evaluate(It.IsAny<DlqMessage>(), It.IsAny<IReadOnlyList<RuleCondition>>()))
            .Returns(new RuleMatchResult { MessageId = 0, ServiceBusMessageId = "any", EntityName = "test-queue", IsMatch = true });

        var options = new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(_dbContext.Database.GetDbConnection()).Options;

        var messageOperations = new Mock<IMessageOperationsService>();
        messageOperations
            .Setup(m => m.ReplayMessageAsync(ns.Id, "test-queue", null, decoy.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                // While this request is still processing the decoy, a different worker (its own
                // context, same connection) claims the target message underneath it.
                await using var racingContext = new DlqDbContext(options);
                var racingCopy = await racingContext.DlqMessages.SingleAsync(m => m.Id == target.Id);
                racingCopy.Status = DlqMessageStatus.Replaying;
                await racingContext.SaveChangesAsync();
                return Result<bool>.Success(true);
            });
        messageOperations
            .Setup(m => m.ReplayMessageAsync(ns.Id, "test-queue", null, target.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        SetUpReplayAllServices(ns, messageOperations);
        SetIntentHeaders(IntentHeaders.IntentReplayAllRules);

        var result = await _controller.ReplayAll(rule.Id);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ReplayAllResponse>().Subject;
        response.Replayed.Should().Be(1, "the decoy replays normally");
        response.Skipped.Should().Be(1, "the target's claim lost the race and must be skipped, not retried");

        // This request's own claim attempt for `target` lost the race — it must never have
        // reached the provider a second time.
        messageOperations.Verify(
            m => m.ReplayMessageAsync(ns.Id, "test-queue", null, target.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var entries = await _dbContext.RecoveryLedgerEntries.AsNoTracking().Where(e => e.DlqMessageId == target.Id).ToListAsync();
        entries.Should().BeEmpty("a lost claim race must never open ledger evidence for an operation this request never performed");
    }
}
