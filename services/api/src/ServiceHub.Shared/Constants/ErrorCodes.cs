namespace ServiceHub.Shared.Constants;

/// <summary>
/// Centralized error codes for the ServiceHub application.
/// Error codes follow the pattern: {Domain}.{Entity}.{Reason}
/// </summary>
public static class ErrorCodes
{
    /// <summary>
    /// Error codes related to namespace operations.
    /// </summary>
    public static class Namespace
    {
        /// <summary>
        /// The namespace name is required.
        /// </summary>
        public const string NameRequired = "Namespace.Name.Required";

        /// <summary>
        /// The namespace name exceeds the maximum allowed length.
        /// </summary>
        public const string NameTooLong = "Namespace.Name.TooLong";

        /// <summary>
        /// The namespace name contains invalid characters.
        /// </summary>
        public const string NameInvalid = "Namespace.Name.Invalid";

        /// <summary>
        /// The connection string is required.
        /// </summary>
        public const string ConnectionStringRequired = "Namespace.ConnectionString.Required";

        /// <summary>
        /// The connection string format is invalid.
        /// </summary>
        public const string ConnectionStringInvalid = "Namespace.ConnectionString.Invalid";

        /// <summary>
        /// The namespace was not found.
        /// </summary>
        public const string NotFound = "Namespace.NotFound";

        /// <summary>
        /// A namespace with the same name already exists.
        /// </summary>
        public const string AlreadyExists = "Namespace.AlreadyExists";

        /// <summary>
        /// The namespace is currently in use and cannot be modified.
        /// </summary>
        public const string InUse = "Namespace.InUse";

        /// <summary>
        /// Failed to connect to the namespace.
        /// </summary>
        public const string ConnectionFailed = "Namespace.Connection.Failed";

        /// <summary>
        /// The namespace endpoint is required.
        /// </summary>
        public const string EndpointRequired = "Namespace.Endpoint.Required";

        /// <summary>
        /// The namespace endpoint format is invalid.
        /// </summary>
        public const string EndpointInvalid = "Namespace.Endpoint.Invalid";
    }

    /// <summary>
    /// Error codes related to message operations.
    /// </summary>
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
        /// The message was not found.
        /// </summary>
        public const string NotFound = "Message.NotFound";

        /// <summary>
        /// The message ID is required.
        /// </summary>
        public const string IdRequired = "Message.Id.Required";

        /// <summary>
        /// The message content type is invalid.
        /// </summary>
        public const string ContentTypeInvalid = "Message.ContentType.Invalid";

        /// <summary>
        /// Failed to send the message.
        /// </summary>
        public const string SendFailed = "Message.Send.Failed";

        /// <summary>
        /// Failed to receive messages.
        /// </summary>
        public const string ReceiveFailed = "Message.Receive.Failed";

        /// <summary>
        /// The message has already been processed.
        /// </summary>
        public const string AlreadyProcessed = "Message.AlreadyProcessed";

        /// <summary>
        /// The queue or topic name is required.
        /// </summary>
        public const string QueueNameRequired = "Message.QueueName.Required";

        /// <summary>
        /// The subscription name is required for topic operations.
        /// </summary>
        public const string SubscriptionNameRequired = "Message.SubscriptionName.Required";
    }

    /// <summary>
    /// Error codes related to queue operations.
    /// </summary>
    public static class Queue
    {
        /// <summary>
        /// The queue was not found.
        /// </summary>
        public const string NotFound = "Queue.NotFound";

        /// <summary>
        /// The queue name is required.
        /// </summary>
        public const string NameRequired = "Queue.Name.Required";

        /// <summary>
        /// The queue name is invalid.
        /// </summary>
        public const string NameInvalid = "Queue.Name.Invalid";

        /// <summary>
        /// A queue with the same name already exists.
        /// </summary>
        public const string AlreadyExists = "Queue.AlreadyExists";

        /// <summary>
        /// Failed to list queues.
        /// </summary>
        public const string ListFailed = "Queue.List.Failed";

        /// <summary>
        /// Failed to get queue.
        /// </summary>
        public const string GetFailed = "Queue.Get.Failed";
    }

    /// <summary>
    /// Error codes related to topic operations.
    /// </summary>
    public static class Topic
    {
        /// <summary>
        /// The topic was not found.
        /// </summary>
        public const string NotFound = "Topic.NotFound";

        /// <summary>
        /// The topic name is required.
        /// </summary>
        public const string NameRequired = "Topic.Name.Required";

        /// <summary>
        /// The topic name is invalid.
        /// </summary>
        public const string NameInvalid = "Topic.Name.Invalid";

        /// <summary>
        /// A topic with the same name already exists.
        /// </summary>
        public const string AlreadyExists = "Topic.AlreadyExists";

        /// <summary>
        /// Failed to list topics.
        /// </summary>
        public const string ListFailed = "Topic.List.Failed";

        /// <summary>
        /// Failed to get topic.
        /// </summary>
        public const string GetFailed = "Topic.Get.Failed";
    }

    /// <summary>
    /// Error codes related to subscription operations.
    /// </summary>
    public static class Subscription
    {
        /// <summary>
        /// The subscription was not found.
        /// </summary>
        public const string NotFound = "Subscription.NotFound";

        /// <summary>
        /// The subscription name is required.
        /// </summary>
        public const string NameRequired = "Subscription.Name.Required";

        /// <summary>
        /// The subscription name is invalid.
        /// </summary>
        public const string NameInvalid = "Subscription.Name.Invalid";

        /// <summary>
        /// A subscription with the same name already exists.
        /// </summary>
        public const string AlreadyExists = "Subscription.AlreadyExists";

        /// <summary>
        /// Failed to list subscriptions.
        /// </summary>
        public const string ListFailed = "Subscription.List.Failed";

        /// <summary>
        /// Failed to get subscription.
        /// </summary>
        public const string GetFailed = "Subscription.Get.Failed";
    }

    /// <summary>
    /// Error codes related to authentication and authorization.
    /// </summary>
    public static class Auth
    {
        /// <summary>
        /// Authentication failed.
        /// </summary>
        public const string Failed = "Auth.Failed";

        /// <summary>
        /// The access token has expired.
        /// </summary>
        public const string TokenExpired = "Auth.Token.Expired";

        /// <summary>
        /// The access token is invalid.
        /// </summary>
        public const string TokenInvalid = "Auth.Token.Invalid";

        /// <summary>
        /// Access is denied.
        /// </summary>
        public const string AccessDenied = "Auth.AccessDenied";

        /// <summary>
        /// Insufficient permissions to perform the operation.
        /// </summary>
        public const string InsufficientPermissions = "Auth.InsufficientPermissions";
    }

    /// <summary>
    /// Error codes related to DLQ Intelligence operations.
    /// </summary>
    public static class Dlq
    {
        /// <summary>
        /// The DLQ message was not found.
        /// </summary>
        public const string NotFound = "Dlq.NotFound";

        /// <summary>
        /// Failed to query DLQ history.
        /// </summary>
        public const string QueryFailed = "Dlq.QueryFailed";

        /// <summary>
        /// Failed to create Service Bus client for DLQ scanning.
        /// </summary>
        public const string ClientFailed = "Dlq.ClientFailed";

        /// <summary>
        /// Failed to export DLQ messages.
        /// </summary>
        public const string ExportFailed = "Dlq.ExportFailed";

        /// <summary>
        /// Failed to generate DLQ summary.
        /// </summary>
        public const string SummaryFailed = "Dlq.SummaryFailed";
    }

    /// <summary>
    /// Error codes related to auto-replay rule operations.
    /// </summary>
    public static class Rule
    {
        /// <summary>The rule was not found.</summary>
        public const string NotFound = "Rule.NotFound";

        /// <summary>The rule name already exists.</summary>
        public const string AlreadyExists = "Rule.AlreadyExists";

        /// <summary>Rule validation failed.</summary>
        public const string ValidationFailed = "Rule.ValidationFailed";

        /// <summary>The rule has exceeded its rate limit.</summary>
        public const string RateLimited = "Rule.RateLimited";

        /// <summary>Failed to test the rule.</summary>
        public const string TestFailed = "Rule.TestFailed";

        /// <summary>Failed to create or update the rule.</summary>
        public const string SaveFailed = "Rule.SaveFailed";

        /// <summary>Failed to delete the rule.</summary>
        public const string DeleteFailed = "Rule.DeleteFailed";
    }

    /// <summary>
    /// General error codes.
    /// </summary>
    public static class General
    {
        /// <summary>
        /// An unexpected error occurred.
        /// </summary>
        public const string UnexpectedError = "General.UnexpectedError";

        /// <summary>
        /// The operation timed out.
        /// </summary>
        public const string Timeout = "General.Timeout";

        /// <summary>
        /// The request rate limit has been exceeded.
        /// </summary>
        public const string RateLimited = "General.RateLimited";

        /// <summary>
        /// The request is invalid.
        /// </summary>
        public const string InvalidRequest = "General.InvalidRequest";

        /// <summary>
        /// One or more validation errors occurred.
        /// </summary>
        public const string ValidationFailed = "General.ValidationFailed";

        /// <summary>
        /// The external service is unavailable.
        /// </summary>
        public const string ServiceUnavailable = "General.ServiceUnavailable";
    }
}
