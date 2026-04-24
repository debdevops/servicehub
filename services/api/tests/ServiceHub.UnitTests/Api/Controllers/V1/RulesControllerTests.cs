using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public class RulesControllerTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly Mock<IRuleEngine> _ruleEngine = new();
    private readonly Mock<ILogger<RulesController>> _logger = new();
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

        _controller = new RulesController(_dbContext, _ruleEngine.Object, _logger.Object);
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
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        _dbContext.AutoReplayRules.Add(CreateRule("Existing Rule"));
        await _dbContext.SaveChangesAsync();

        var request = CreateRuleRequest("Existing Rule");
        var result = await _controller.Create(request);
        result.Result.Should().NotBeOfType<CreatedAtActionResult>();
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
    }

    [Fact]
    public async Task Update_NotFound_ReturnsError()
    {
        var request = CreateRuleRequest();
        var result = await _controller.Update(999, request);
        result.Result.Should().NotBeOfType<OkObjectResult>();
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
    }

    [Fact]
    public async Task Delete_NotFound_ReturnsError()
    {
        var result = await _controller.Delete(999);
        result.Should().NotBeOfType<NoContentResult>();
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
}
