using System.Text.Json;
using Amazon.SQS.Model;

namespace ServiceHub.Providers.Aws;

/// <summary>
/// Reads the failure reason AWS itself wrote on a dead letter that reached SQS from Lambda or EventBridge (build unit G3, plan 3a.1).
/// The shapes were captured from a real account on 2026-11-11, one failure of each kind:
/// <list type="bullet">
/// <item><b>EventBridge target dead-letter queue</b>: the body is the original event; the reason is in the message attributes
/// <c>ERROR_CODE</c> and <c>ERROR_MESSAGE</c>, next to <c>RULE_ARN</c> and <c>TARGET_ARN</c>.</item>
/// <item><b>Lambda dead-letter queue</b> (the function's <c>DeadLetterConfig</c>): the body is the original payload; the attributes are
/// <c>RequestID</c>, <c>ErrorCode</c> (a status number, "200" when the function itself threw) and <c>ErrorMessage</c>.</item>
/// <item><b>Lambda on-failure destination</b>: no attributes; the body is a JSON record with <c>requestContext.condition</c> and, for an
/// asynchronous invoke where the function threw, <c>responsePayload.errorType</c> and <c>responsePayload.errorMessage</c>. A stream
/// mapping's record (Kinesis, DynamoDB) has the condition only.</item>
/// <item><b>SNS subscription dead-letter queue</b>: the body is the SNS notification and <b>nothing says why delivery failed</b>. There is
/// no reason to read, so this class returns nothing for it.</item>
/// </list>
/// A message of any other shape reads as nothing, never as an error.
/// </summary>
internal static class AwsDeadLetterEnvelope
{
    /// <summary>Longest description kept. A Lambda error message has no fixed limit; the row and the signature need the start of it.</summary>
    internal const int MaxDescriptionLength = 1000;

    internal sealed record Finding(string Reason, string? Description);

    internal static Finding? Read(string? body, IReadOnlyDictionary<string, MessageAttributeValue>? attributes)
    {
        return FromEventBridge(attributes) ?? FromLambdaDeadLetterQueue(attributes) ?? FromLambdaDestination(body);
    }

    private static Finding? FromEventBridge(IReadOnlyDictionary<string, MessageAttributeValue>? attributes)
    {
        // RULE_ARN / TARGET_ARN are what make this EventBridge's stamp and not an application's own ERROR_CODE attribute.
        if (Value(attributes, "RULE_ARN") is null && Value(attributes, "TARGET_ARN") is null)
        {
            return null;
        }

        var code = Value(attributes, "ERROR_CODE");
        return code is null ? null : new Finding(code, Trim(Value(attributes, "ERROR_MESSAGE")));
    }

    private static Finding? FromLambdaDeadLetterQueue(IReadOnlyDictionary<string, MessageAttributeValue>? attributes)
    {
        if (Value(attributes, "RequestID") is null)
        {
            return null;
        }

        var code = Value(attributes, "ErrorCode");
        var message = Value(attributes, "ErrorMessage");
        if (code is null && message is null)
        {
            return null;
        }

        // "200" means Lambda ran the function and the function threw. Any other number is Lambda failing to run it (throttled, no access).
        var reason = code is null or "200" ? "Lambda function error" : $"Lambda invoke error {code}";
        return new Finding(reason, Trim(message));
    }

    private static Finding? FromLambdaDestination(string? body)
    {
        if (string.IsNullOrEmpty(body) || body[0] != '{' || !body.Contains("\"requestContext\"", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var request = Child(root, "requestContext");

            // A stream mapping's failure record (Kinesis, DynamoDB) has the same requestContext and no responsePayload: its reason is the condition.
            var response = Child(root, "responsePayload");
            var reason = Text(response, "errorType") ?? Text(request, "condition");
            return reason is null ? null : new Finding(reason, Trim(Text(response, "errorMessage")));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Value(IReadOnlyDictionary<string, MessageAttributeValue>? attributes, string name) =>
        attributes is not null && attributes.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value?.StringValue)
            ? value.StringValue
            : null;

    private static JsonElement Child(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var child) ? child : default;

    private static string? Text(JsonElement parent, string name)
    {
        var child = Child(parent, name);
        return child.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(child.GetString()) ? child.GetString() : null;
    }

    private static string? Trim(string? text) =>
        text is { Length: > MaxDescriptionLength } ? text[..MaxDescriptionLength] : text;
}
