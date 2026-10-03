using FluentAssertions;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.Models;

namespace ServiceHub.UnitTests.Api;

/// <summary>The API answers in queue / topic / subscription — an adapter's own wording must never reach a screen.</summary>
public sealed class EntityKindTests
{
    [Theory]
    [InlineData("Queue", "queue")]
    [InlineData("sns topic", "topic")]
    [InlineData("SNS Topic", "topic")]
    [InlineData("Topic", "topic")]
    [InlineData("Subscription", "subscription")]
    [InlineData("pubsub subscription", "subscription")]
    [InlineData("sqs queue", "queue")]
    public void Kind_is_always_one_of_the_three_words(string adapterWord, string expected) =>
        NamespacesController.Kind(new CloudEntity { EntityType = adapterWord }).Should().Be(expected);
}
