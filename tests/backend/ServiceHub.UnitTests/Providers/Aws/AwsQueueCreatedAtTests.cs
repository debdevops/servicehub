using FluentAssertions;
using ServiceHub.Providers.Aws;

namespace ServiceHub.UnitTests.Infrastructure.Aws;

/// <summary>
/// Live 2026-09-30 (machine on IST): the AWS SDK returns a queue's <c>CreatedTimestamp</c> as a local-kind
/// <see cref="DateTime"/>; relabelling it UTC put the queue's creation 5½ hours in the future, so every dead letter seen
/// afterwards looked like it belonged to an earlier queue and was auto-resolved as "vanished".
/// </summary>
public sealed class AwsQueueCreatedAtTests
{
    private static readonly DateTime CreatedUtc = new(2026, 9, 30, 16, 37, 27, DateTimeKind.Utc);

    [Fact]
    public void A_local_kind_timestamp_keeps_its_real_instant_whatever_the_machines_zone()
    {
        // What SDK 3.7 returns: the same instant, expressed as local time.
        AwsMessagingProvider.ToCreatedAt(CreatedUtc.ToLocalTime()).Should().Be(new DateTimeOffset(CreatedUtc));
    }

    [Fact]
    public void A_utc_kind_timestamp_is_kept_as_it_is()
    {
        AwsMessagingProvider.ToCreatedAt(CreatedUtc).Should().Be(new DateTimeOffset(CreatedUtc));
    }

    [Fact]
    public void An_epoch_zero_timestamp_means_the_attribute_did_not_come_back()
    {
        AwsMessagingProvider.ToCreatedAt(DateTime.UnixEpoch).Should().BeNull();
        AwsMessagingProvider.ToCreatedAt(DateTime.UnixEpoch.ToLocalTime()).Should().BeNull();
    }
}
