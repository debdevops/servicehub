using FluentAssertions;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Infrastructure.Agent;

namespace ServiceHub.UnitTests.Infrastructure.Agent;

public sealed class ReasoningEvidenceMapperTests
{
    private static IncidentDetailResponse CreateIncident(
        Guid namespaceId,
        string signatureHash,
        IReadOnlyList<RecoveryLedgerEntryResponse> recoveryEntries)
    {
        var summary = new IncidentSummary(
            RecoveryEntryCount: 3,
            OpenRecoveryEntryCount: 1,
            PendingDecisionCount: 2,
            AnomalyFlagCount: 1,
            DriftFindingCount: 0,
            CorrelationHypothesisCount: 0,
            PreventionTriggerCount: 0,
            ReplayPlanCount: 0);

        return new IncidentDetailResponse(
            SignatureHash: signatureHash,
            NamespaceId: namespaceId,
            NamespaceName: "orders-namespace",
            LifecycleStatus: "Active",
            FirstSeenAt: DateTimeOffset.UtcNow.AddDays(-1),
            LastSeenAt: DateTimeOffset.UtcNow,
            OccurrenceCount: 7,
            DominantDeadletterReason: "MaxDeliveryCountExceeded",
            TopTerms: ["timeout", "downstream"],
            Summary: summary,
            RecoveryEntries: recoveryEntries,
            PlaybookEntries: []);
    }

    private static RecoveryLedgerEntryResponse CreateRecoveryEntry(string providerSnapshot) => new(
        Id: Guid.NewGuid(),
        OperationId: Guid.NewGuid(),
        DlqMessageId: 1,
        NamespaceId: Guid.NewGuid(),
        NamespaceNameSnapshot: "orders-namespace",
        ProviderSnapshot: providerSnapshot,
        EnvironmentSnapshot: "Production",
        EntityNameSnapshot: "orders-queue",
        EntityTypeSnapshot: "Queue",
        TopicNameSnapshot: null,
        BodyHash: "deadbeef",
        FailureCategorySnapshot: "Transient",
        DeadLetterReasonSnapshot: "MaxDeliveryCountExceeded",
        SignatureHashSnapshot: "sig-1",
        TargetEntity: "orders-queue",
        BegunAt: DateTimeOffset.UtcNow,
        MarkerApplied: true,
        State: "Observing",
        Disposition: null,
        VerificationResult: null,
        VerificationConfidence: null,
        ObservationWindowEndsAt: null,
        ClosedAt: null);

    [Fact]
    public void BuildRef_CombinesNamespaceIdAndSignatureHash()
    {
        var namespaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var @ref = ReasoningEvidenceMapper.BuildRef(namespaceId, "sig-1");

        @ref.Should().Be("11111111-1111-1111-1111-111111111111:sig-1");
    }

    [Fact]
    public void ToEvidenceRecord_MapsIdentityAndSummaryFields()
    {
        var namespaceId = Guid.NewGuid();
        var incident = CreateIncident(namespaceId, "sig-1", []);

        var record = ReasoningEvidenceMapper.ToEvidenceRecord("entra:owner-1", "Critical", isRecurring: true, incident);

        record.Ref.Should().Be(ReasoningEvidenceMapper.BuildRef(namespaceId, "sig-1"));
        record.OwnerId.Should().Be("entra:owner-1");
        record.NamespaceId.Should().Be(namespaceId);
        record.SignatureHash.Should().Be("sig-1");
        record.LifecycleStatus.Should().Be("Active");
        record.Severity.Should().Be("Critical");
        record.IsRecurring.Should().BeTrue();
        record.DominantDeadletterReason.Should().Be("MaxDeliveryCountExceeded");
        record.TopTerms.Should().BeEquivalentTo(["timeout", "downstream"]);
        record.OccurrenceCount.Should().Be(7);
        record.BlastRadius.Should().Be(7);
        record.PendingDecisionCount.Should().Be(2);
        record.RecoveryEntryCount.Should().Be(3);
        record.OpenRecoveryEntryCount.Should().Be(1);
        record.AnomalyFlagCount.Should().Be(1);
    }

    [Fact]
    public void ToEvidenceRecord_NoRecoveryEntries_ProviderIsNull()
    {
        var incident = CreateIncident(Guid.NewGuid(), "sig-1", []);

        var record = ReasoningEvidenceMapper.ToEvidenceRecord("entra:owner-1", "Warning", isRecurring: false, incident);

        record.Provider.Should().BeNull();
    }

    [Fact]
    public void ToEvidenceRecord_WithRecoveryEntries_ProviderComesFromFirstEntry()
    {
        var incident = CreateIncident(Guid.NewGuid(), "sig-1", [CreateRecoveryEntry("AzureServiceBus")]);

        var record = ReasoningEvidenceMapper.ToEvidenceRecord("entra:owner-1", "Warning", isRecurring: false, incident);

        record.Provider.Should().Be("AzureServiceBus");
    }

    [Fact]
    public void ToEvidenceRecord_NullIncident_Throws()
    {
        var act = () => ReasoningEvidenceMapper.ToEvidenceRecord("entra:owner-1", "Warning", isRecurring: false, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
