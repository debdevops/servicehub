using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Agents;
using ServiceHub.Infrastructure.DlqObserver;

namespace ServiceHub.UnitTests.Agents;

/// <summary>
/// ADR-0018: how a replay's window closes on a cloud that cannot prove a fix by itself, once that namespace has a live whole
/// view of its dead-letter queue. The rule under test: a fact, or "cannot be proven" — never a guess, and never "verified"
/// from a view that could not answer.
/// </summary>
public sealed class DeadLetterViewVerdictTests
{
    private const string Owner = "owner1";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-04T12:00:00Z");

    private static Namespace Ns(CloudProviderType provider = CloudProviderType.Aws) =>
        Namespace.Create("sqs.eu-west-1.amazonaws.com", "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENGbPxRfiCYEXAMPLEKEY", provider: provider, awsRegion: "eu-west-1", ownerId: Owner).Value;

    private static RecoveryLedgerEntry Entry(Namespace ns, TimeSpan windowEndedAgo) => new()
    {
        OperationId = Guid.NewGuid(), OwnerId = Owner, NamespaceId = ns.Id, BodyHash = "h", TargetEntity = "orders", BegunAt = Now.AddHours(-30),
        State = RecoveryEntryState.Observing, ObservationWindowEndsAt = Now - windowEndedAgo, RecoveryMarker = "m", MarkerApplied = true,
    };

    private static INamespaceRepository Repo(Namespace ns) =>
        Mock.Of<INamespaceRepository>(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()) == Task.FromResult(Result<Namespace>.Success(ns)));

    private static ICloudProviderRouter Router(bool provesByItself = false)
    {
        var capabilities = ProviderCapabilities.Aws with { CanProveDlqAbsence = provesByItself };
        var provider = Mock.Of<ICloudMessagingProvider>(p => p.Capabilities == capabilities);
        return Mock.Of<ICloudProviderRouter>(r => r.IsRegistered(It.IsAny<CloudProviderType>()) && r.Resolve(It.IsAny<CloudProviderType>()) == provider);
    }

    private static IDlqObserverAttestationService Attested(Namespace ns, bool live = true, bool enabled = true) =>
        Mock.Of<IDlqObserverAttestationService>(a => a.GetAsync(Owner, ns.Id, It.IsAny<CancellationToken>()) == Task.FromResult<DlqObserverAttestation?>(
            new DlqObserverAttestation { OwnerId = Owner, NamespaceId = ns.Id, Enabled = enabled, ObserverReference = "x", DlqEntityName = "*", StalenessBoundMinutes = 30, LastConfirmedAt = live ? Now.AddMinutes(-1) : Now.AddHours(-5) }));

    private static IDeadLetterReturnCheck Check(DeadLetterReturnVerdict verdict, CloudProviderType provider = CloudProviderType.Aws)
    {
        var check = new Mock<IDeadLetterReturnCheck>();
        check.SetupGet(c => c.Provider).Returns(provider);
        check.Setup(c => c.CheckAsync(It.IsAny<Namespace>(), It.IsAny<DlqObserverAttestation>(), It.IsAny<RecoveryLedgerEntry>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>())).ReturnsAsync(verdict);
        return check.Object;
    }

    private static Task<DeadLetterViewVerdict.Decision> Decide(Namespace ns, RecoveryLedgerEntry entry, IDeadLetterReturnCheck? check, IDlqObserverAttestationService? attestation = null, ICloudProviderRouter? router = null) =>
        DeadLetterViewVerdict.DecideAsync(entry, Repo(ns), router ?? Router(), attestation ?? Attested(ns), check is null ? [] : [check], new DeadLetterViewTracker(), Now, NullLogger.Instance, CancellationToken.None);

    [Fact]
    public async Task Not_returned_closes_as_verified_with_a_reason_named_from_the_namespace()
    {
        var ns = Ns();
        var d = await Decide(ns, Entry(ns, TimeSpan.FromMinutes(1)), Check(DeadLetterReturnVerdict.NotReturned));
        d.Should().Be(new DeadLetterViewVerdict.Decision(true, false, RecoveryObservationOutcome.NoRecurrenceObserved, "AWS_VIEW_CONFIRMED_ABSENCE"));
    }

    [Fact]
    public async Task Returned_closes_as_came_back_with_exact_confidence()
    {
        var ns = Ns();
        var d = await Decide(ns, Entry(ns, TimeSpan.FromMinutes(1)), Check(DeadLetterReturnVerdict.Returned));
        d.Should().Be(new DeadLetterViewVerdict.Decision(true, false, RecoveryObservationOutcome.RecurrenceObserved, "AWS_VIEW_CONFIRMED_RETURN", VerificationConfidence.Exact));
    }

    [Fact]
    public async Task Too_early_leaves_the_entry_open()
    {
        var ns = Ns();
        (await Decide(ns, Entry(ns, TimeSpan.FromMinutes(1)), Check(DeadLetterReturnVerdict.TooEarly))).LeaveOpen.Should().BeTrue();
        // …however long ago the window ended: "too early" is never turned into a verdict by waiting.
        (await Decide(ns, Entry(ns, TimeSpan.FromHours(20)), Check(DeadLetterReturnVerdict.TooEarly))).LeaveOpen.Should().BeTrue();
    }

    [Fact]
    public async Task Cannot_tell_is_asked_again_for_a_while_and_then_closes_as_cannot_be_proven_never_as_verified()
    {
        var ns = Ns();
        var check = Check(DeadLetterReturnVerdict.CannotTell("FIFO_QUEUE"));

        (await Decide(ns, Entry(ns, TimeSpan.FromHours(1)), check)).LeaveOpen.Should().BeTrue();

        var late = await Decide(ns, Entry(ns, DeadLetterViewVerdict.GiveUpAfter + TimeSpan.FromMinutes(1)), check);
        late.Should().Be(new DeadLetterViewVerdict.Decision(true, false, RecoveryObservationOutcome.ObservationUnavailable, "AWS_FIFO_QUEUE"));
    }

    [Fact]
    public async Task A_check_that_throws_fails_closed()
    {
        var ns = Ns();
        var check = new Mock<IDeadLetterReturnCheck>();
        check.SetupGet(c => c.Provider).Returns(CloudProviderType.Aws);
        check.Setup(c => c.CheckAsync(It.IsAny<Namespace>(), It.IsAny<DlqObserverAttestation>(), It.IsAny<RecoveryLedgerEntry>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

        var d = await Decide(ns, Entry(ns, DeadLetterViewVerdict.GiveUpAfter + TimeSpan.FromMinutes(1)), check.Object);

        d.Outcome.Should().Be(RecoveryObservationOutcome.ObservationUnavailable);
        d.Reason.Should().Be("AWS_VIEW_CHECK_FAILED");
    }

    [Fact]
    public async Task Without_a_live_switched_on_view_nothing_changes_the_old_path_decides()
    {
        var ns = Ns();
        var entry = Entry(ns, TimeSpan.FromMinutes(1));
        var verified = Check(DeadLetterReturnVerdict.NotReturned);

        (await Decide(ns, entry, verified, Attested(ns, live: false))).Handled.Should().BeFalse("a stale view confirms nothing");
        (await Decide(ns, entry, verified, Attested(ns, enabled: false))).Handled.Should().BeFalse("a view nobody switched on confirms nothing");
        (await Decide(ns, entry, verified, Mock.Of<IDlqObserverAttestationService>())).Handled.Should().BeFalse("no attestation at all");
        (await Decide(ns, entry, check: null)).Handled.Should().BeFalse("no check for this cloud");
        (await Decide(ns, entry, Check(DeadLetterReturnVerdict.NotReturned, CloudProviderType.Gcp))).Handled.Should().BeFalse("another cloud's check is not this cloud's");
        (await Decide(ns, entry, verified, router: Router(provesByItself: true))).Handled.Should().BeFalse("a cloud that proves it by itself never needs the view");
    }

    [Fact]
    public void Live_since_starts_on_the_first_confirmation_and_again_after_the_view_is_lost()
    {
        var tracker = new DeadLetterViewTracker();
        var id = Guid.NewGuid();
        tracker.LiveSince(Owner, id).Should().BeNull();
        tracker.Confirmed(Owner, id, Now).Should().Be(Now);
        tracker.Confirmed(Owner, id, Now.AddMinutes(5)).Should().Be(Now, "later confirmations do not move the start");
        tracker.Lost(Owner, id);
        tracker.LiveSince(Owner, id).Should().BeNull();
        tracker.Confirmed(Owner, id, Now.AddHours(1)).Should().Be(Now.AddHours(1));
    }
}
