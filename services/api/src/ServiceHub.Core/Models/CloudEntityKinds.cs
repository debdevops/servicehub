namespace ServiceHub.Core.Models;

/// <summary>
/// The contract's three words for an entity: queue, topic or subscription. An adapter may describe an entity in its own
/// service's terms ("sns topic"); everything above the adapter answers in these words.
/// </summary>
public static class CloudEntityKinds
{
    /// <summary>The entity's kind: <c>subscription</c>, <c>topic</c> or <c>queue</c>; anything else comes back lower-cased and unchanged.</summary>
    public static string Of(CloudEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var raw = entity.EntityType.Trim().ToLowerInvariant();
        return raw.Contains("subscription", StringComparison.Ordinal) ? "subscription"
            : raw.Contains("topic", StringComparison.Ordinal) ? "topic"
            : raw.Contains("queue", StringComparison.Ordinal) ? "queue"
            : raw;
    }
}
