namespace ServiceHub.Core.Constants;

/// <summary>
/// Stable, machine-readable error codes. Every <c>ProblemDetails</c> ServiceHub returns carries one
/// of these plus a human sentence (ARCHITECTURE §5.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>A code is a promise.</b> Once a version has shipped with one, it keeps its meaning: clients,
/// runbooks and the escalation messages that quote a gate's reason code all depend on it. Add
/// codes; do not repurpose them.
/// </para>
/// <para>
/// The catalogue grows with the screens that raise the failures (rule R12), so a code here
/// means something in this product.
/// </para>
/// </remarks>
public static class ErrorCodes
{
    /// <summary>An unhandled failure. The detail is deliberately generic; the log has the rest.</summary>
    public const string UnexpectedFailure = "unexpected_failure";

    /// <summary>The request was malformed or failed validation.</summary>
    public const string ValidationFailed = "validation_failed";

    /// <summary>The thing being asked about does not exist, or is not visible to this caller.</summary>
    public const string NotFound = "not_found";

    /// <summary>
    /// The caller is known but not permitted. The response says what is missing and who can grant
    /// it — never a bare 403 (IA §7).
    /// </summary>
    public const string PermissionDenied = "permission_denied";

    /// <summary>
    /// The provider backing this namespace genuinely cannot do what was asked. This is a capability
    /// fact, not a fault — and it always travels with the remedy, where one exists (rule R4).
    /// </summary>
    public const string CapabilityUnavailable = "capability_unavailable";

    /// <summary>
    /// A dangerous request arrived without the header that says the caller meant it. The response
    /// names the header, so the fix is one sentence.
    /// </summary>
    public const string IntentRequired = "intent_required";

    /// <summary>An <c>X-API-KEY</c> was presented that is not a configured key.</summary>
    public const string InvalidApiKey = "invalid_api_key";

    /// <summary>Codes for failures that belong to no single screen.</summary>
    /// <remarks>Raised by <c>ConnectionStringProtector</c>.</remarks>
    public static class General
    {
        /// <summary>An unexpected error occurred.</summary>
        public const string UnexpectedError = "General.UnexpectedError";
        /// <summary>
        /// The request is invalid.
        /// </summary>
        public const string InvalidRequest = "General.InvalidRequest";

        /// <summary>
        /// The external service is unavailable.
        /// </summary>
        public const string ServiceUnavailable = "General.ServiceUnavailable";
    }

    /// <summary>Codes raised while connecting and describing a namespace (unit 1.1 onwards).</summary>
    /// <remarks>Values are stable so runbooks and clients that quote them keep working.</remarks>
    public static class Namespace
    {
        /// <summary>The namespace name is required.</summary>
        public const string NameRequired = "Namespace.Name.Required";

        /// <summary>The namespace name, display name or description is too long.</summary>
        public const string NameTooLong = "Namespace.Name.TooLong";

        /// <summary>The namespace name contains invalid characters or has an invalid format.</summary>
        public const string NameInvalid = "Namespace.Name.Invalid";

        /// <summary>The connection string is required.</summary>
        public const string ConnectionStringRequired = "Namespace.ConnectionString.Required";

        /// <summary>The connection string format is invalid.</summary>
        public const string ConnectionStringInvalid = "Namespace.ConnectionString.Invalid";
        /// <summary>
        /// Failed to connect to the namespace.
        /// </summary>
        public const string ConnectionFailed = "Namespace.Connection.Failed";

        /// <summary>
        /// The namespace endpoint format is invalid.
        /// </summary>
        public const string EndpointInvalid = "Namespace.Endpoint.Invalid";

        /// <summary>
        /// The namespace was not found.
        /// </summary>
        public const string NotFound = "Namespace.NotFound";

        /// <summary>A namespace with this name (or ID) already exists for the owner.</summary>
        public const string AlreadyExists = "Namespace.AlreadyExists";
    }

    /// <summary>Codes raised while reading queues.</summary>
    public static class Queue
    {
        /// <summary>
        /// Failed to get queue.
        /// </summary>
        public const string GetFailed = "Queue.Get.Failed";

        /// <summary>
        /// Failed to list queues.
        /// </summary>
        public const string ListFailed = "Queue.List.Failed";

        /// <summary>
        /// The queue was not found.
        /// </summary>
        public const string NotFound = "Queue.NotFound";
    }

    /// <summary>Codes raised while reading topics.</summary>
    public static class Topic
    {
        /// <summary>
        /// Failed to get topic.
        /// </summary>
        public const string GetFailed = "Topic.Get.Failed";

        /// <summary>
        /// Failed to list topics.
        /// </summary>
        public const string ListFailed = "Topic.List.Failed";

        /// <summary>
        /// The topic was not found.
        /// </summary>
        public const string NotFound = "Topic.NotFound";
    }

    /// <summary>Codes raised while reading subscriptions.</summary>
    public static class Subscription
    {
        /// <summary>
        /// Failed to get subscription.
        /// </summary>
        public const string GetFailed = "Subscription.Get.Failed";

        /// <summary>
        /// Failed to list subscriptions.
        /// </summary>
        public const string ListFailed = "Subscription.List.Failed";

        /// <summary>
        /// The subscription was not found.
        /// </summary>
        public const string NotFound = "Subscription.NotFound";
    }

    /// <summary>Codes raised while sending, receiving and inspecting messages.</summary>
    public static class Message
    {
        /// <summary>
        /// The message body is required.
        /// </summary>
        public const string BodyRequired = "Message.Body.Required";

        /// <summary>
        /// The message body exceeds the maximum allowed size.
        /// </summary>
        public const string BodyTooLarge = "Message.Body.TooLarge";

        /// <summary>
        /// The provider does not support retrieving message counts.
        /// </summary>
        public const string CountUnsupported = "Message.Operation.CountUnsupported";

        /// <summary>
        /// The provider does not support manual dead-lettering.
        /// </summary>
        public const string DeadLetterUnsupported = "Message.Operation.DeadLetterUnsupported";

        /// <summary>
        /// The message was not found.
        /// </summary>
        public const string NotFound = "Message.NotFound";

        /// <summary>
        /// The provider does not support the purge operation.
        /// </summary>
        public const string PurgeUnsupported = "Message.Operation.PurgeUnsupported";

        /// <summary>
        /// The queue or topic name is required.
        /// </summary>
        public const string QueueNameRequired = "Message.QueueName.Required";

        /// <summary>
        /// Failed to receive messages.
        /// </summary>
        public const string ReceiveFailed = "Message.Receive.Failed";

        /// <summary>
        /// Failed to cancel a scheduled message.
        /// </summary>
        public const string ScheduledCancelFailed = "Message.Scheduled.CancelFailed";

        /// <summary>
        /// Failed to list scheduled messages.
        /// </summary>
        public const string ScheduledListFailed = "Message.Scheduled.ListFailed";

        /// <summary>
        /// The provider does not support scheduled messages.
        /// </summary>
        public const string ScheduledUnsupported = "Message.Operation.ScheduledUnsupported";

        /// <summary>
        /// Failed to send the message.
        /// </summary>
        public const string SendFailed = "Message.Send.Failed";
    }
}
