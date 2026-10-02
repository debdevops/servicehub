using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Agents;
using ServiceHub.Infrastructure.DlqObserver;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Agents;

/// <summary>
/// Unit 4.2: the canary is the only thing that can make an observer "live", and it fails closed — a canary the observer's own
/// log has not shown never confirms anything, and a canary that cannot be tracked is never recorded as sent.
/// </summary>
public sealed class DlqObserverCanaryAgentTests : IDisposable
{
    private const string Owner = "owner1";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Namespace _ns = Namespace.CreateWithManagedIdentity("aws-dev", provider: CloudProviderType.Aws, awsRegion: "us-east-1", ownerId: Owner).Value;
    private readonly Mock<IMessageOperationsService> _sender = new();
    private readonly Mock<IDlqObserverLogReader> _reader = new();
    private readonly Clock _clock = new();

    public DlqObserverCanaryAgentTests()
    {
        _connection.Open();
        using var db = NewDb();
        db.Database.Migrate();
        _reader.SetupGet(r => r.Provider).Returns(CloudProviderType.Aws);
    }

    public void Dispose() => _connection.Dispose();

    private ServiceHubDbContext NewDb() => new(new DbContextOptionsBuilder<ServiceHubDbContext>().UseSqlite(_connection).Options);

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private DlqObserverCanaryAgent Agent(bool withReader = true)
    {
        var namespaces = new Mock<INamespaceRepository>();
        namespaces.Setup(n => n.GetByIdAsync(_ns.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(_ns));

        var services = new ServiceCollection();
        services.AddScoped(_ => NewDb());
        services.AddScoped<IDlqObserverAttestationService>(sp => new DlqObserverAttestationService(sp.GetRequiredService<ServiceHubDbContext>()));
        services.AddSingleton(namespaces.Object);
        services.AddSingleton(_sender.Object);
        if (withReader) services.AddSingleton(_reader.Object);
        return new DlqObserverCanaryAgent(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<DlqObserverCanaryAgent>.Instance, _clock);
    }

    private async Task Configure(bool enabled = true)
    {
        await using var db = NewDb();
        (await new DlqObserverAttestationService(db).ConfigureAsync(Owner, _ns.Id, enabled, "observer-table", "orders-dlq", 30)).IsSuccess.Should().BeTrue();
    }

    private async Task<DlqObserverAttestation> Row()
    {
        await using var db = NewDb();
        return (await new DlqObserverAttestationService(db).GetAsync(Owner, _ns.Id))!;
    }

    private void SenderReturns(string? id) =>
        _sender.Setup(s => s.SendReturningProviderIdAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<string?>(id));

    [Fact]
    public async Task With_no_observer_turned_on_it_does_nothing()
    {
        await Configure(enabled: false);

        var result = await Agent().ExecuteCycleAsync(CancellationToken.None);

        result.Changed.Should().Be(0);
        _sender.Verify(s => s.SendReturningProviderIdAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_new_observer_gets_a_canary_in_the_named_dlq_and_is_not_live_until_the_log_shows_it()
    {
        await Configure();
        SenderReturns("sqs-msg-1");

        var result = await Agent().ExecuteCycleAsync(CancellationToken.None);

        result.Changed.Should().Be(1);
        _sender.Verify(s => s.SendReturningProviderIdAsync(
            It.Is<SendMessageRequest>(r => r.NamespaceId == _ns.Id && r.EntityName == "orders-dlq"
                && r.ApplicationProperties!.ContainsKey(DlqObserverCanaryAgent.CanaryProperty)),
            It.IsAny<CancellationToken>()), Times.Once);
        var row = await Row();
        row.LastCanaryMessageId.Should().Be("sqs-msg-1");
        row.IsLiveAt(_clock.Now).Should().BeFalse("sending a canary proves nothing — only the observer's log can");
    }

    [Fact]
    public async Task A_canary_the_log_shows_makes_the_observer_live()
    {
        await Configure();
        SenderReturns("sqs-msg-1");
        var agent = Agent();
        await agent.ExecuteCycleAsync(CancellationToken.None);
        _reader.Setup(r => r.HasRecordedArrivalAsync(_ns, "observer-table", "sqs-msg-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await agent.ExecuteCycleAsync(CancellationToken.None);

        result.Summary.Should().Contain("confirmed live");
        (await Row()).IsLiveAt(DateTimeOffset.UtcNow).Should().BeTrue();
    }

    [Fact]
    public async Task A_canary_the_log_has_not_shown_never_confirms_and_is_not_resent_within_the_beat()
    {
        await Configure();
        SenderReturns("sqs-msg-1");
        var agent = Agent();
        await agent.ExecuteCycleAsync(CancellationToken.None);
        _reader.Setup(r => r.HasRecordedArrivalAsync(It.IsAny<Namespace>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await agent.ExecuteCycleAsync(CancellationToken.None);

        result.Summary.Should().Contain("waiting");
        (await Row()).LastConfirmedAt.Should().BeNull();
        _sender.Verify(s => s.SendReturningProviderIdAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_unanswered_canary_is_replaced_after_the_beat_and_the_observer_stays_not_live()
    {
        await Configure();
        SenderReturns("sqs-msg-1");
        var agent = Agent();
        await agent.ExecuteCycleAsync(CancellationToken.None);
        _reader.Setup(r => r.HasRecordedArrivalAsync(It.IsAny<Namespace>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        SenderReturns("sqs-msg-2");
        _clock.Now += TimeSpan.FromMinutes(11); // a third of the 30-minute bound is 10

        await agent.ExecuteCycleAsync(CancellationToken.None);

        var row = await Row();
        row.LastCanaryMessageId.Should().Be("sqs-msg-2");
        row.LastConfirmedAt.Should().BeNull();
    }

    [Fact]
    public async Task A_canary_the_cloud_gave_no_id_for_is_never_recorded_as_sent()
    {
        await Configure();
        SenderReturns(null);

        var result = await Agent().ExecuteCycleAsync(CancellationToken.None);

        result.Degraded.Should().BeTrue();
        (await Row()).LastCanaryMessageId.Should().BeNull();
    }

    [Fact]
    public async Task With_no_log_reader_for_the_cloud_nothing_is_sent_and_the_cycle_says_it_could_not_check()
    {
        await Configure();
        SenderReturns("sqs-msg-1");

        var result = await Agent(withReader: false).ExecuteCycleAsync(CancellationToken.None);

        result.Degraded.Should().BeTrue();
        _sender.Verify(s => s.SendReturningProviderIdAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void It_declares_that_it_acts_so_the_agents_screen_leads_with_it()
    {
        var descriptor = Agent().Descriptor;

        descriptor.Authority.Should().Be(AgentAuthority.ActsWithApproval);
        descriptor.Kind.Should().Be(AgentKind.Act);
        descriptor.CanAct.Should().BeTrue();
    }
}
