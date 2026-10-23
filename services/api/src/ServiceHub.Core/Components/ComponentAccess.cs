using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Components;

/// <summary>How a component is reached from ServiceHub (Add a component dialog, plan §3). Derived from the catalogue, never stored.</summary>
public enum ComponentAccess
{
    /// <summary>It lives in the account, region or project already connected. Prerequisites and a check; no box to type in.</summary>
    SameConnection,

    /// <summary>It is a separate resource with its own address or key. The credential core (encrypted, write-only) is what keeps it.</summary>
    OwnCredential,

    /// <summary>Nothing reads it yet (no adapter, or on request). Shown, never tickable.</summary>
    NotBuildable,

    /// <summary>Needs no tick of its own: it is read as another component.</summary>
    Included,
}

/// <summary>One copy-ready block in a setup guide: a policy or a command.</summary>
/// <param name="Label">What it is.</param>
/// <param name="Text">The text to copy.</param>
public sealed record SetupSnippet(string Label, string Text);

/// <summary>What a person must do before ticking a component, and what ServiceHub will ask of the cloud.</summary>
/// <param name="What">What the component is, in one sentence.</param>
/// <param name="Appears">Where it shows up in ServiceHub once it is on.</param>
/// <param name="Needs">One plain sentence: the least ServiceHub must be allowed to do.</param>
/// <param name="Steps">Numbered prerequisites, in order.</param>
/// <param name="Snippets">Copy-ready policies or commands the steps refer to.</param>
/// <param name="Permissions">The rights the check tests, by their cloud names.</param>
/// <param name="IncludedIn">When set, the component is read as this other component and needs no tick.</param>
public sealed record ComponentSetup(
    string What, string Appears, string Needs, IReadOnlyList<string> Steps, IReadOnlyList<SetupSnippet> Snippets, IReadOnlyList<string> Permissions, string? IncludedIn = null);

/// <summary>
/// What the Add a component dialog lists, and how each entry is reached. The words in <see cref="Setups"/> are the only copy of the
/// prerequisites; the dialog shows them and a test keeps every listable component covered.
/// </summary>
public static class ComponentAccessRules
{
    private static readonly string[] None = [];

    /// <summary>The prerequisites per <see cref="ComponentDescriptor.ComponentKey"/>. A key with no entry is listed with no guidance.</summary>
    public static IReadOnlyDictionary<string, ComponentSetup> Setups { get; } = new Dictionary<string, ComponentSetup>(StringComparer.Ordinal)
    {
        ["step-functions"] = new(
            "State machine runs that ended failed, timed out or aborted, with the error and cause the run recorded.", "Appears in the menu as Step Functions. Read only: a failed run is handed to a person, who re-runs it in the AWS console.", "states:ListStateMachines, states:ListExecutions and states:DescribeExecution. Nothing that starts or stops a run.",
            ["Add this policy to the identity ServiceHub signs in as. ListStateMachines cannot be limited to one machine; the other two can.", "Press Check access."],
            [new("IAM policy", """
                {
                  "Version": "2012-10-17",
                  "Statement": [{
                    "Effect": "Allow",
                    "Action": ["states:ListStateMachines", "states:ListExecutions", "states:DescribeExecution"],
                    "Resource": "*"
                  }]
                }
                """)],
            ["states:ListStateMachines", "states:ListExecutions", "states:DescribeExecution"]),
        ["workflows"] = new(
            "Workflow executions that ended failed or cancelled, with the error each recorded. Found across every location.", "Appears in the menu as Workflows. Read only: a failed run is handed to a person, who re-runs it in the Google Cloud console.", "Workflows Viewer (roles/workflows.viewer) and the Workflows APIs switched on in the project.",
            ["Switch on the two Workflows APIs in the project.", "Let the identity ServiceHub signs in as read workflows.", "Press Check access."],
            [new("Enable the APIs", "gcloud services enable workflows.googleapis.com workflowexecutions.googleapis.com --project=<project-id>"),
             new("Grant the viewer role", "gcloud projects add-iam-policy-binding <project-id> --member=serviceAccount:<the identity> --role=roles/workflows.viewer")],
            ["roles/workflows.viewer", "workflows.googleapis.com", "workflowexecutions.googleapis.com"]),
        ["logic-apps"] = new(
            "Logic App runs that ended failed, timed out or cancelled, with the error code and message each recorded. Found in every subscription the identity can see.", "Appears in the menu as Logic Apps. Read only: a failed run is handed to a person, who re-runs it in the Azure portal.", "Reader on the subscription (Microsoft.Logic/workflows/read and Microsoft.Logic/workflows/runs/read), signed in with Entra. Nothing that runs, resubmits or cancels a run.",
            ["Use an Azure connection that signs in with Entra (Managed Identity or Default Azure Credential). A Service Bus connection string cannot read Logic Apps.", "Give that identity the Reader role on the subscription that holds the Logic Apps.", "Press Check access."],
            [new("Grant the reader role", "az role assignment create --assignee <the identity> --role Reader --scope /subscriptions/<subscription-id>")],
            ["Reader", "Microsoft.Logic/workflows/read", "Microsoft.Logic/workflows/runs/read"]),
        ["event-grid"] = new(
            "Events Event Grid could not deliver, kept as blobs in the dead-letter container of an event subscription. Each is listed with the reason and the delivery attempts.", "Appears in the menu as Event Grid. Read only: a dead-lettered event is handed to a person. The event body is not shown.", "Reader on the subscription to find event subscriptions, and Storage Blob Data Reader on the storage account that holds the dead-letter container. Signed in with Entra.",
            ["Use an Azure connection that signs in with Entra (Managed Identity or Default Azure Credential).", "Give that identity the Reader role on the subscription.", "Give it Storage Blob Data Reader on the storage account that holds the dead-letter container.", "Press Check access."],
            [new("Grant the reader role", "az role assignment create --assignee <the identity> --role Reader --scope /subscriptions/<subscription-id>"),
             new("Grant blob read", "az role assignment create --assignee <the identity> --role \"Storage Blob Data Reader\" --scope <storage account resource id>")],
            ["Reader", "Microsoft.EventGrid/eventSubscriptions/read", "Storage Blob Data Reader"]),
        ["aws-streams"] = new(
            "Failure records that Lambda writes to an on-failure queue when it cannot process a batch from a Kinesis or DynamoDB stream. Each points at the batch (stream, shard, sequence numbers); the original records are not copied.", "Appears in the menu as Kinesis, DynamoDB streams. Read only: a failure record is handed to a person. The original record expires with the stream and may already be gone.", "lambda:ListEventSourceMappings and sqs:GetQueueAttributes and sqs:ReceiveMessage on the on-failure queue. Nothing that deletes or sends.",
            ["Set an on-failure destination (an SQS queue) on the Lambda event source mapping that reads the stream.", "Add this policy to the identity ServiceHub signs in as. ReceiveMessage is how a queue is looked at; it deletes nothing, but it counts as a receive, so do not put a redrive policy on the on-failure queue.", "Press Check access."],
            [new("IAM policy", """
                {
                  "Version": "2012-10-17",
                  "Statement": [
                    { "Effect": "Allow", "Action": ["lambda:ListEventSourceMappings"], "Resource": "*" },
                    { "Effect": "Allow", "Action": ["sqs:GetQueueAttributes", "sqs:ReceiveMessage"], "Resource": "<on-failure queue arn>" }
                  ]
                }
                """)],
            ["lambda:ListEventSourceMappings", "sqs:GetQueueAttributes", "sqs:ReceiveMessage"]),
        ["eventarc"] = new(
            "Events Eventarc could not deliver, kept in the dead-letter topic of the trigger's Pub/Sub subscription. A trigger whose subscription has no dead-letter policy keeps nothing, and the page says so.", "Appears in the menu as Eventarc. Read only: an undelivered event is handed to a person. The event body is not shown.", "Eventarc Viewer (roles/eventarc.viewer) to list triggers, and Pub/Sub Viewer plus Subscriber on the dead-letter topic's subscription to look at what it holds.",
            ["Switch on the Eventarc API in the project.", "Give the identity ServiceHub signs in as the Eventarc Viewer role.", "Put a dead-letter policy on the trigger's transport subscription, and a pull subscription on the dead-letter topic so something can be looked at.", "Press Check access."],
            [new("Enable the API", "gcloud services enable eventarc.googleapis.com --project=<project-id>"),
             new("Grant the viewer role", "gcloud projects add-iam-policy-binding <project-id> --member=serviceAccount:<the identity> --role=roles/eventarc.viewer")],
            ["roles/eventarc.viewer", "roles/pubsub.viewer", "roles/pubsub.subscriber", "eventarc.googleapis.com"]),
        ["sqs"] = new("Messages SQS moved to a dead-letter queue after too many failed receives.", "Appears in the menu as SQS. Its messages join Needs your attention, Failure signatures and Replay history.", "List and read queues. To replay: send and delete messages.", None, [], ["List and read", "Send and Delete (replay only)"]),
        ["sns"] = new("Messages that SNS could not deliver to a queue subscriber, kept in that subscriber’s dead-letter queue.", "Appears in the menu as SNS, with its topics and subscriptions. Replays go to the one subscriber queue.", "The same as SQS, plus listing topics and subscriptions.", None, [], ["List and read", "List topics and subscriptions"]),
        ["pubsub"] = new("Messages Pub/Sub moved to a dead-letter topic after too many delivery attempts.", "Appears in the menu as Pub/Sub. Its messages join Needs your attention, Failure signatures and Replay history.", "Pub/Sub Viewer to see. Publisher and Subscriber to replay.", None, [], ["Pub/Sub Viewer", "Publisher and Subscriber (replay only)"]),
        ["service-bus"] = new("Messages that Service Bus gave up on. Each one is listed with its reason so a person or an approved rule can put it back.", "Appears in the menu as Service Bus. Its messages join Needs your attention, Failure signatures and Replay history.", "Listen to read. Listen and Send to replay.", None, [], ["Listen", "Send (replay only)"]),
        ["lambda-eventbridge"] = new("Failures from a Lambda or an EventBridge rule that end up in an SQS dead-letter queue.", "Needs no tick of its own: they already show as SQS dead letters, with the reason.", "Needs no tick of its own: their failures already show as SQS dead letters, with the reason.", None, [], None, IncludedIn: "sqs"),
    };

    /// <summary>The setup for a component key, or null when none is written.</summary>
    public static ComponentSetup? SetupFor(string componentKey) => Setups.TryGetValue(componentKey, out var s) ? s : null;

    /// <summary>
    /// Whether a row is read on demand through an <see cref="IComponentSource"/> instead of watched through a messaging adapter: a run
    /// history or a store of failed records. These are the rows a person ticks, and the ones the menu lists only where they are on.
    /// </summary>
    public static bool ReadsOnDemand(ComponentDescriptor row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Detection is DetectionKind.Runs or DetectionKind.Records;
    }

    /// <summary>How a catalogue row is reached.</summary>
    public static ComponentAccess Of(ComponentDescriptor row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (SetupFor(row.ComponentKey)?.IncludedIn is not null)
        {
            return ComponentAccess.Included;
        }

        if (row.Status is SupportStatus.OnRequest or SupportStatus.NotSupported || row.Provider is null)
        {
            return ComponentAccess.NotBuildable;
        }

        if (ReadsOnDemand(row))
        {
            return ComponentAccess.SameConnection;
        }

        // An Azure connection is one Service Bus namespace's own connection string: a separate resource, kept by the credential core.
        return row.Provider == CloudProviderType.Azure ? ComponentAccess.OwnCredential : ComponentAccess.SameConnection;
    }

    /// <summary>
    /// The entries the dialog lists for a cloud: one per component key, the first row that reads something (a store or a run history),
    /// plus not-supported groups are left out (they are one line of text). Signal-only rows belong to a component listed by its store row.
    /// </summary>
    public static IReadOnlyList<ComponentDescriptor> Listed(CloudKind cloud) =>
    [
        .. ComponentCatalog.ForCloud(cloud)
            .Where(c => c.Status != SupportStatus.NotSupported && c.Detection is DetectionKind.Store or DetectionKind.Runs or DetectionKind.Records)
            .GroupBy(c => c.ComponentKey)
            .Select(g => g.First()),
    ];

    /// <summary>
    /// Whether ServiceHub already watches a component on every connection of its adapter without being ticked: a Supported message
    /// store. Existing installs keep working, and the dialog shows it ticked and locked (removing the connection removes it).
    /// </summary>
    public static bool IsAlwaysOn(ComponentDescriptor row) =>
        Of(row) is ComponentAccess.SameConnection or ComponentAccess.OwnCredential
        && row.Detection == DetectionKind.Store
        && ComponentCatalog.ForCloud(row.Cloud).Any(c => c.ComponentKey == row.ComponentKey && c.Status == SupportStatus.Supported);
}
