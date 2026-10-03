namespace ServiceHub.Core.Models;

/// <summary>
/// What a Slack / Teams / webhook message about an escalation says (unit 5.5): what stopped, where, why, and what to do —
/// never just "an escalation occurred".
/// </summary>
/// <param name="Kind"><c>approval</c> — a replay waits for a person · <c>agent</c> — an agent stopped working.</param>
/// <param name="ReasonCode">The gate's code, kept beside the words.</param>
/// <param name="Reason">One plain sentence, with what to do.</param>
/// <param name="NamespaceName">Where, if a namespace.</param>
/// <param name="Provider">azure · aws · gcp, if known.</param>
/// <param name="Entity">The queue or subscription, if one.</param>
/// <param name="RaisedAtUtc">When.</param>
/// <param name="ReviewUrl">A link that opens the answer in ServiceHub, when <c>Webhooks:PublicUrl</c> is set.</param>
public sealed record EscalationNotification(
    string Kind, string ReasonCode, string Reason, string? NamespaceName, string? Provider, string? Entity, DateTimeOffset RaisedAtUtc, string? ReviewUrl)
{
    /// <summary>"The Agent stopped and asked you" / "An agent stopped working".</summary>
    public string Headline => Kind switch
    {
        "agent" => "An agent stopped working",
        "test" => "Test message from ServiceHub",
        _ => "The Agent stopped and asked you",
    };

    /// <summary>"AWS · orders-dev · orders-sqs" — whatever is known, in that order.</summary>
    public string Where => string.Join(" · ", new[] { ProviderLabel(Provider), NamespaceName, Entity }.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>What the person should do next, in words.</summary>
    public string WhatToDo => Kind switch
    {
        "agent" => "Open Agents in ServiceHub to see its last error.",
        "test" => "Nothing — this only checks that the channel works.",
        _ => "Open ServiceHub and review it: approve replays it through the same safety checks, or decline with a reason.",
    };

    private static string? ProviderLabel(string? p) => p switch { "azure" => "Azure", "aws" => "AWS", "gcp" => "Google Cloud", _ => p };
}
