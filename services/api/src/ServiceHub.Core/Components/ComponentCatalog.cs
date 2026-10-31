using System.Text;
using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Components;

/// <summary>
/// The one list of what ServiceHub covers (5.0.0 plan §6a). The in-app coverage page, the menu, the README matrix and the
/// demo are all built from it, so they cannot disagree. Adding a component is one entry here plus its adapter.
/// </summary>
/// <remarks>
/// This file is the only place outside a provider adapter that may tie a component to a
/// <see cref="CloudProviderType"/>. Everything else asks the catalogue (rule R4).
/// A row may be <see cref="SupportStatus.Supported"/> only after a recorded run on a real cloud; the tests check that every
/// Supported row names evidence that exists.
/// </remarks>
public static class ComponentCatalog
{
    private const string Evidence = "docs/RECOVERY-EVIDENCE.md";

    private static readonly string[] Queue = ["queue"];
    private static readonly string[] QueueAndSubscription = ["queue", "topic", "subscription"];
    private static readonly string[] TopicAndSubscription = ["topic", "subscription"];

    private static readonly RecoverabilityFacts HeldProvable = new(CopyKind.Held, RecoveryPathKind.Adapter, ProofLevel.NotReturned);
    private static readonly RecoverabilityFacts HeldUnproven = new(CopyKind.Held, RecoveryPathKind.Adapter, ProofLevel.Accepted);
    private static readonly RecoverabilityFacts HeldNoPath = new(CopyKind.Held, RecoveryPathKind.None, ProofLevel.None);
    private static readonly RecoverabilityFacts SignalOnly = new(CopyKind.None, RecoveryPathKind.None, ProofLevel.None, HasSignal: true);
    private static readonly RecoverabilityFacts Nothing = RecoverabilityFacts.Unknown;

    /// <summary>Every row, in the order the coverage page shows them.</summary>
    public static IReadOnlyList<ComponentDescriptor> All { get; } =
    [
        // ── Azure ────────────────────────────────────────────────────────────────────────────────
        new("service-bus.dead-letter", "service-bus", CloudKind.Azure, "Service Bus", "Dead-letter queue", DetectionKind.Store,
            "ServiceHub replays one message and verifies it", HeldProvable, SupportStatus.Supported, "Can reach Verified",
            CloudProviderType.Azure, QueueAndSubscription, InSidebar: true, EvidenceRef: Evidence),
        new("service-bus.transfer-dead-letter", "service-bus", CloudKind.Azure, "Service Bus", "Transfer dead-letter queue", DetectionKind.Store,
            "Shown as a count; reading it is being validated", HeldNoPath, SupportStatus.BeingValidated, "Replay target needs a live check",
            CloudProviderType.Azure, QueueAndSubscription, PlanUnit: "3a.2"),
        new("service-bus.stuck", "service-bus", CloudKind.Azure, "Service Bus", "Deferred messages, stuck sessions", DetectionKind.Signal,
            "Handed to a person", SignalOnly, SupportStatus.BeingValidated, "No replay offered",
            CloudProviderType.Azure, QueueAndSubscription, PlanUnit: "3.2"),
        new("storage-queues.poison", "storage-queues", CloudKind.Azure, "Storage Queues", "Poison queue", DetectionKind.Store,
            "ServiceHub replays one message", HeldUnproven, SupportStatus.BeingValidated, "Not yet validated",
            InSidebar: true, PlanUnit: "4.1"),
        new("event-grid.dead-letter-blobs", "event-grid", CloudKind.Azure, "Event Grid", "Dead-letter blobs", DetectionKind.Records,
            "Handed to a person", HeldNoPath, SupportStatus.BeingValidated,
            "Read only: the reason and delivery attempts of each dead-lettered event. ServiceHub cannot retry these: Event Grid has no way to redeliver an event, and republishing one to the topic would reach every subscription. Needs an Azure connection that signs in with Entra",
            CloudProviderType.Azure, InSidebar: true, PlanUnit: "PP4a"),
        new("logic-apps.failed-runs", "logic-apps", CloudKind.Azure, "Logic Apps", "Failed runs", DetectionKind.Runs,
            "A person asks Azure to resubmit the run", new(CopyKind.None, RecoveryPathKind.Resubmit, ProofLevel.None), SupportStatus.Supported,
            "Failed, timed-out and cancelled runs. A resubmit starts a new run from the trigger and every step runs again, including steps that already succeeded; the original run stays Failed. Ends Unverified. Needs an Azure connection that signs in with Entra",
            CloudProviderType.Azure, InSidebar: true, EvidenceRef: Evidence, PlanUnit: "PP4d"),
        new("azure.no-failed-copy", "azure-streams", CloudKind.Azure, "Event Hubs, Stream Analytics, Data Factory, Synapse, IoT Hub", "No failed copy is kept",
            DetectionKind.Signal, "Not supported", SignalOnly, SupportStatus.NotSupported, "Only a lag or job metric exists; your monitoring already alerts on it"),
        new("azure.consumers", "azure-compute", CloudKind.Azure, "Function Apps, Container Apps, AKS, Web Apps, App Service", "Not a message store",
            DetectionKind.None, "Not supported", Nothing, SupportStatus.NotSupported, "They are consumers; their failures reach ServiceHub through the queue they read"),
        new("azure.not-messaging", "azure-data", CloudKind.Azure, "Cosmos DB, Azure SQL, Storage Accounts, Data Lake, Key Vault, API Management", "Not messaging",
            DetectionKind.None, "Not supported", Nothing, SupportStatus.NotSupported, "No message, so nothing to put back"),

        // ── AWS ──────────────────────────────────────────────────────────────────────────────────
        new("sqs.dead-letter", "sqs", CloudKind.Aws, "SQS", "Dead-letter queue", DetectionKind.Store,
            "ServiceHub replays one message, on approval", HeldUnproven, SupportStatus.Supported, "Ends Unverified",
            CloudProviderType.Aws, Queue, InSidebar: true, EvidenceRef: Evidence),
        new("sqs.ageing", "sqs", CloudKind.Aws, "SQS", "Ageing source queue", DetectionKind.Signal,
            "Handed to a person", SignalOnly, SupportStatus.BeingValidated, "No replay offered", CloudProviderType.Aws, Queue, PlanUnit: "3.2"),
        new("sns.subscriber-dead-letter", "sns", CloudKind.Aws, "SNS", "Dead-letter queue of each subscriber queue", DetectionKind.Store,
            "ServiceHub replays to the one subscriber queue", HeldUnproven, SupportStatus.Supported, "Queue subscribers only. Ends Unverified",
            CloudProviderType.Aws, TopicAndSubscription, InSidebar: true, EvidenceRef: Evidence),
        new("lambda-eventbridge.reason", "lambda-eventbridge", CloudKind.Aws, "Lambda, EventBridge", "Dead letters that land in SQS", DetectionKind.Store,
            "Reason shown; replay on request", HeldNoPath, SupportStatus.BeingValidated, "Read as SQS dead letters", CloudProviderType.Aws, Queue, PlanUnit: "3a.1"),
        new("step-functions.failed-runs", "step-functions", CloudKind.Aws, "Step Functions", "Failed runs", DetectionKind.Runs,
            "A person asks AWS to redrive the run", new(CopyKind.None, RecoveryPathKind.Native, ProofLevel.None), SupportStatus.Supported,
            "Failed, timed-out and aborted Standard runs; AWS restarts the run from the step that failed. Ends Unverified. Express runs are not listed",
            CloudProviderType.Aws, InSidebar: true, EvidenceRef: Evidence, PlanUnit: "PP2"),
        new("aws-streams.failure-records", "aws-streams", CloudKind.Aws, "Kinesis, DynamoDB streams", "Failure records point at the original", DetectionKind.Records,
            "Handed to a person", new(CopyKind.Pointer, RecoveryPathKind.None, ProofLevel.None), SupportStatus.BeingValidated,
            "Read only: where a failed batch was in the stream. The original record expires, and may already be gone",
            CloudProviderType.Aws, InSidebar: true, PlanUnit: "PP4c"),
        new("aws.no-failed-copy", "aws-brokers", CloudKind.Aws, "Data Firehose, MSK, Amazon MQ", "No failed copy, or a connected broker",
            DetectionKind.Signal, "Not supported", SignalOnly, SupportStatus.NotSupported, "Brokers are out of scope"),
        new("aws.not-messaging", "aws-other", CloudKind.Aws, "ECS, EKS, S3, DynamoDB, RDS, ElastiCache, API Gateway", "Not messaging",
            DetectionKind.None, "Not supported", Nothing, SupportStatus.NotSupported, "No message, so nothing to put back"),

        // ── Google Cloud ─────────────────────────────────────────────────────────────────────────
        new("pubsub.dead-letter", "pubsub", CloudKind.Gcp, "Pub/Sub", "Dead-letter topic of each subscription", DetectionKind.Store,
            "ServiceHub replays to the one subscription, on approval", HeldUnproven, SupportStatus.Supported, "No counts. Ends Unverified",
            CloudProviderType.Gcp, TopicAndSubscription, InSidebar: true, EvidenceRef: Evidence),
        new("pubsub.backlog", "pubsub", CloudKind.Gcp, "Pub/Sub", "Backlog, oldest unacked age", DetectionKind.Signal,
            "Handed to a person", SignalOnly, SupportStatus.BeingValidated, "No replay offered", CloudProviderType.Gcp, TopicAndSubscription, PlanUnit: "3.2"),
        new("eventarc.undelivered", "eventarc", CloudKind.Gcp, "Eventarc", "Undelivered events", DetectionKind.Records,
            "A person retries it in Pub/Sub", HeldNoPath, SupportStatus.BeingValidated,
            "Read only: events held in a trigger's dead-letter topic. Retry one in Pub/Sub, which puts it back on this trigger's own transport topic only. A trigger with no dead-letter policy keeps nothing",
            CloudProviderType.Gcp, InSidebar: true, PlanUnit: "PP4b"),
        new("workflows.failed-runs", "workflows", CloudKind.Gcp, "Workflows", "Failed runs", DetectionKind.Runs,
            "Handed to a person", new(CopyKind.None, RecoveryPathKind.Resubmit, ProofLevel.None), SupportStatus.BeingValidated,
            "Read only: failed and cancelled runs. Re-run in the Google Cloud console", CloudProviderType.Gcp, InSidebar: true, PlanUnit: "PP3"),
        new("cloud-scheduler.last-attempt", "cloud-scheduler", CloudKind.Gcp, "Cloud Scheduler", "Jobs whose last attempt failed", DetectionKind.Runs,
            "Detected, handed to an owner", SignalOnly, SupportStatus.BeingValidated,
            "Read only: the last attempt of each job, not a history. A job that failed and then succeeded shows nothing. A paused job is listed, never counted as failed",
            CloudProviderType.Gcp, InSidebar: true, PlanUnit: "DO1"),
        new("gcp.no-failed-copy", "gcp-streams", CloudKind.Gcp, "Dataflow, Dataproc, BigQuery, Cloud Tasks", "No failed copy is kept",
            DetectionKind.Signal, "Not supported", SignalOnly, SupportStatus.NotSupported, "Only a job or lag metric exists"),
        new("gcp.not-messaging", "gcp-other", CloudKind.Gcp, "Cloud Functions, Cloud Run, GKE, Cloud Storage, Firestore, Spanner, Apigee", "Not messaging",
            DetectionKind.None, "Not supported", Nothing, SupportStatus.NotSupported, "No message, so nothing to put back"),
    ];

    /// <summary>The rows listed under one cloud.</summary>
    public static IEnumerable<ComponentDescriptor> ForCloud(CloudKind cloud) => All.Where(c => c.Cloud == cloud);

    /// <summary>The components the menu lists for a cloud: one per component key, in catalogue order.</summary>
    public static IEnumerable<ComponentDescriptor> Sidebar(CloudKind cloud) =>
        ForCloud(cloud).Where(c => c.InSidebar).DistinctBy(c => c.ComponentKey);

    /// <summary>A row by its id, or null. An unknown id is never guessed at.</summary>
    public static ComponentDescriptor? Find(string id) => All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// The dead-letter row an adapter's entity belongs to, or null when the catalogue has none. Null means
    /// <see cref="RecoverabilityClass.R0Unknown"/>: no replay, a person decides.
    /// </summary>
    /// <param name="provider">The adapter the namespace uses.</param>
    /// <param name="entityKind">The API's own word: <c>queue</c>, <c>topic</c> or <c>subscription</c>.</param>
    public static ComponentDescriptor? ForEntity(CloudProviderType provider, string entityKind) =>
        All.FirstOrDefault(c => c.Provider == provider
            && c.Detection == DetectionKind.Store
            && c.Status == SupportStatus.Supported
            && (c.EntityKinds?.Contains(entityKind, StringComparer.OrdinalIgnoreCase) ?? false));

    /// <summary>The cloud an adapter belongs to, read from the catalogue. Null when no row names the adapter.</summary>
    public static CloudKind? CloudOf(CloudProviderType provider) => All.FirstOrDefault(c => c.Provider == provider)?.Cloud;

    /// <summary>The coverage table as Markdown, for the README. A test keeps the README copy equal to this (build-plan unit 0.1).</summary>
    public static string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Cloud | Component | Where it gets stuck | Found by | Moves on by | Status | Note |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var c in All)
        {
            sb.Append("| ").Append(CloudName(c.Cloud)).Append(" | ").Append(c.Name).Append(" | ").Append(c.StuckPlace).Append(" | ")
                .Append(DetectionWords(c.Detection)).Append(" | ").Append(c.MovesOnBy).Append(" | ").Append(StatusWords(c.Status)).Append(" | ")
                .Append(c.Note ?? string.Empty).AppendLine(" |");
        }

        return sb.ToString();
    }

    /// <summary>The cloud's name as shown on screen.</summary>
    public static string CloudName(CloudKind cloud) => cloud switch
    {
        CloudKind.Azure => "Azure",
        CloudKind.Aws => "AWS",
        _ => "Google Cloud",
    };

    /// <summary>The words for how something is found.</summary>
    public static string DetectionWords(DetectionKind kind) => kind switch
    {
        DetectionKind.Store => "Reads the place that holds the failed message",
        DetectionKind.Runs => "Reads the history of failed runs",
        DetectionKind.Records => "Reads the failed records the service keeps",
        DetectionKind.Signal => "A metric or a state only",
        _ => "—",
    };

    /// <summary>The words for a status.</summary>
    public static string StatusWords(SupportStatus status) => status switch
    {
        SupportStatus.Supported => "Supported",
        SupportStatus.BeingValidated => "Being validated",
        SupportStatus.OnRequest => "On request",
        _ => "Not supported",
    };
}
