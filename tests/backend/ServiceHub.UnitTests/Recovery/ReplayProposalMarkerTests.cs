using System.Text.Json;
using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Recovery;

namespace ServiceHub.UnitTests.Recovery;

/// <summary>
/// The replay proposal must not promise a recovery ID the cloud cannot add. Found live 2026-11-10: an SQS message already at
/// ten attributes was proposed with "carries a recovery ID", and the replay then (honestly) reported that none was attached.
/// </summary>
public sealed class ReplayProposalMarkerTests
{
    private static DlqMessage With(int properties, CloudProviderType cloud = CloudProviderType.Aws) => new()
    {
        MessageId = "m", SequenceNumber = 1, BodyHash = "h", NamespaceId = Guid.NewGuid(), OwnerId = "o", EntityName = "orders",
        EntityType = ServiceBusEntityType.Queue, CloudProvider = cloud, EnqueuedTimeUtc = DateTimeOffset.UtcNow, DetectedAtUtc = DateTimeOffset.UtcNow,
        ApplicationPropertiesJson = properties == 0 ? null : JsonSerializer.Serialize(Enumerable.Range(0, properties).ToDictionary(i => $"k{i}", i => "v")),
    };

    [Theory]
    [InlineData(0, true)]
    [InlineData(9, true)]
    [InlineData(10, false)]
    [InlineData(12, false)]
    public void On_a_cloud_with_an_attribute_limit_a_full_message_has_no_room(int properties, bool room) =>
        DlqReplayService.HasRoomForMarker(With(properties), ProviderCapabilities.Aws).Should().Be(room);

    [Fact]
    public void A_cloud_with_no_attribute_limit_always_has_room()
    {
        DlqReplayService.HasRoomForMarker(With(40, CloudProviderType.Azure), ProviderCapabilities.Azure).Should().BeTrue();
        DlqReplayService.HasRoomForMarker(With(40, CloudProviderType.Gcp), ProviderCapabilities.Gcp).Should().BeTrue();
    }

    [Fact]
    public void Properties_that_cannot_be_read_leave_the_clouds_general_answer_standing()
    {
        var message = With(0);
        var broken = new DlqMessage
        {
            MessageId = message.MessageId, SequenceNumber = 1, BodyHash = "h", NamespaceId = message.NamespaceId, OwnerId = "o", EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue, CloudProvider = CloudProviderType.Aws, EnqueuedTimeUtc = message.EnqueuedTimeUtc, DetectedAtUtc = message.DetectedAtUtc,
            ApplicationPropertiesJson = "{not json",
        };
        DlqReplayService.HasRoomForMarker(broken, ProviderCapabilities.Aws).Should().BeTrue();
    }
}
