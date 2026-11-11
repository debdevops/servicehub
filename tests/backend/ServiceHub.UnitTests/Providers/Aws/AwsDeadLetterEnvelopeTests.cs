using Amazon.SQS.Model;
using FluentAssertions;
using ServiceHub.Providers.Aws;

namespace ServiceHub.UnitTests.Infrastructure.Aws;

/// <summary>
/// G3. Every body and attribute set here is what a real account returned on 2026-11-11 (one failure of each kind, read from one SQS queue),
/// with the account number replaced. If AWS changes a shape these tests still pass; the live capture is the thing to repeat.
/// </summary>
public sealed class AwsDeadLetterEnvelopeTests
{
    private const string LambdaDestinationBody =
        """{"version":"1.0","timestamp":"2026-11-11T06:22:06.386Z","requestContext":{"requestId":"e4090552-6962-4161-8075-0db09ed5d263","functionArn":"arn:aws:lambda:ap-south-1:123456789012:function:servicehub-g3-tmp-fail:$LATEST","condition":"RetriesExhausted","approximateInvokeCount":1},"requestPayload":{"orderId":"G3-LAMBDA-1","amount":42},"responseContext":{"statusCode":200,"executedVersion":"$LATEST","functionError":"Unhandled"},"responsePayload":{"errorMessage": "G3 capture: payment gateway refused order", "errorType": "ValueError", "requestId": "e4090552-6962-4161-8075-0db09ed5d263", "stackTrace": ["  File \"/var/task/index.py\", line 2, in handler\n    raise ValueError(\"G3 capture: payment gateway refused order\")\n"]}}""";

    private const string EventBridgeBody =
        """{"version":"0","id":"f345da82-ebe5-afe7-9149-0e5a7426f85b","detail-type":"OrderPlaced","source":"servicehub.g3","account":"123456789012","time":"2026-11-11T06:21:51Z","region":"ap-south-1","resources":[],"detail":{"orderId":"G3-EB-1","amount":42}}""";

    private const string SnsBody =
        """
        {
          "Type" : "Notification",
          "MessageId" : "9e6a9b52-3550-51b9-89e1-f83c3eeef2fa",
          "TopicArn" : "arn:aws:sns:ap-south-1:123456789012:servicehub-g3-tmp",
          "Message" : "{\"orderId\":\"G3-SNS-1\",\"amount\":42}",
          "Timestamp" : "2026-11-11T06:21:50.809Z",
          "SignatureVersion" : "1",
          "MessageAttributes" : { "tenant" : {"Type":"String","Value":"acme"} }
        }
        """;

    private static Dictionary<string, MessageAttributeValue> Attributes(params (string Name, string Value, string Type)[] items) =>
        items.ToDictionary(i => i.Name, i => new MessageAttributeValue { DataType = i.Type, StringValue = i.Value });

    [Fact]
    public void EventBridgeTargetDeadLetter_ReadsCodeAndMessageFromTheAttributes()
    {
        var attributes = Attributes(
            ("ERROR_CODE", "NO_PERMISSIONS", "String"),
            ("ERROR_MESSAGE", "User: events.amazonaws.com is not authorized to perform: sqs:sendmessage on resource: arn:aws:sqs:ap-south-1:123456789012:servicehub-g3-tmp-noaccess because no resource-based policy allows the sqs:sendmessage action", "String"),
            ("RULE_ARN", "arn:aws:events:ap-south-1:123456789012:rule/servicehub-g3-tmp", "String"),
            ("TARGET_ARN", "arn:aws:sqs:ap-south-1:123456789012:servicehub-g3-tmp-noaccess", "String"));

        var finding = AwsDeadLetterEnvelope.Read(EventBridgeBody, attributes);

        finding!.Reason.Should().Be("NO_PERMISSIONS");
        finding.Description.Should().StartWith("User: events.amazonaws.com is not authorized");
    }

    [Fact]
    public void LambdaDeadLetterQueue_ReadsTheErrorMessageAttribute()
    {
        var attributes = Attributes(
            ("ErrorCode", "200", "Number"),
            ("ErrorMessage", "G3 capture: payment gateway refused order", "String"),
            ("RequestID", "e4090552-6962-4161-8075-0db09ed5d263", "String"));

        var finding = AwsDeadLetterEnvelope.Read("""{"orderId":"G3-LAMBDA-1","amount":42}""", attributes);

        finding!.Reason.Should().Be("Lambda function error");
        finding.Description.Should().Be("G3 capture: payment gateway refused order");
    }

    [Fact]
    public void LambdaDeadLetterQueue_ACodeOtherThan200_IsAnInvokeErrorNotAFunctionError()
    {
        var attributes = Attributes(("ErrorCode", "429", "Number"), ("RequestID", "r-1", "String"));

        AwsDeadLetterEnvelope.Read("{}", attributes)!.Reason.Should().Be("Lambda invoke error 429");
    }

    [Fact]
    public void LambdaOnFailureDestination_ReadsErrorTypeAndMessageFromTheBody()
    {
        var finding = AwsDeadLetterEnvelope.Read(LambdaDestinationBody, new Dictionary<string, MessageAttributeValue>());

        finding!.Reason.Should().Be("ValueError");
        finding.Description.Should().Be("G3 capture: payment gateway refused order");
    }

    [Fact]
    public void LambdaOnFailureDestination_WithNoResponsePayload_FallsBackToTheCondition()
    {
        const string body = """{"version":"1.0","requestContext":{"requestId":"r","functionArn":"arn:aws:lambda:ap-south-1:123456789012:function:f:$LATEST","condition":"EventAgeExceeded","approximateInvokeCount":1},"requestPayload":{}}""";

        var finding = AwsDeadLetterEnvelope.Read(body, null);

        finding!.Reason.Should().Be("EventAgeExceeded");
        finding.Description.Should().BeNull();
    }

    [Fact]
    public void SnsSubscriptionDeadLetter_HasNoReasonToRead()
    {
        // Captured fact: SNS writes the notification and no account of why delivery failed, in the body or in the attributes.
        AwsDeadLetterEnvelope.Read(SnsBody, new Dictionary<string, MessageAttributeValue>()).Should().BeNull();
    }

    [Fact]
    public void AStreamFailureRecord_ReadsTheCondition()
    {
        // The livetest Kinesis mapping's real record: same requestContext, no responsePayload.
        const string body = """{"requestContext":{"requestId":"858e1039-eaf9-4928-8068-fac770b901aa","functionArn":"arn:aws:lambda:ap-south-1:123456789012:function:servicehub-livetest-fail","condition":"RetryAttemptsExhausted","approximateInvokeCount":1},"responseContext":{"statusCode":200,"executedVersion":"$LATEST","functionError":"Unhandled"},"version":"1.0","timestamp":"2026-11-11T04:29:00.466Z","KinesisBatchInfo":{"shardId":"shardId-000000000000","batchSize":1,"streamArn":"arn:aws:kinesis:ap-south-1:123456789012:stream/servicehub-livetest-orders"}}""";

        var finding = AwsDeadLetterEnvelope.Read(body, null);

        finding!.Reason.Should().Be("RetryAttemptsExhausted");
        finding.Description.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("{ not json \"requestContext\"")]
    [InlineData("""{"orderId":"A-1"}""")]
    public void AnOrdinaryMessage_ReadsAsNothing(string? body)
    {
        AwsDeadLetterEnvelope.Read(body, new Dictionary<string, MessageAttributeValue>()).Should().BeNull();
    }

    [Fact]
    public void AnApplicationsOwnErrorCodeAttribute_IsNotTakenForEventBridge()
    {
        var attributes = Attributes(("ERROR_CODE", "E42", "String"), ("ErrorMessage", "app text", "String"));

        AwsDeadLetterEnvelope.Read("{}", attributes).Should().BeNull();
    }

    [Fact]
    public void ALongErrorMessage_IsCut()
    {
        var attributes = Attributes(("ErrorMessage", new string('x', 5000), "String"), ("RequestID", "r-1", "String"));

        AwsDeadLetterEnvelope.Read("{}", attributes)!.Description.Should().HaveLength(AwsDeadLetterEnvelope.MaxDescriptionLength);
    }
}
