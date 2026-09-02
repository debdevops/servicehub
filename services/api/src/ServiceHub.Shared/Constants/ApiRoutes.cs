namespace ServiceHub.Shared.Constants;

/// <summary>
/// Centralized API route constants for the ServiceHub application.
/// Provides consistent route definitions across the application.
/// </summary>
public static class ApiRoutes
{
    /// <summary>
    /// The base path for all API endpoints.
    /// </summary>
    public const string Base = "api";

    /// <summary>
    /// The API version prefix.
    /// </summary>
    public const string Version = "v1";

    /// <summary>
    /// The versioned API base path.
    /// </summary>
    public const string VersionedBase = $"{Base}/{Version}";

    /// <summary>
    /// Routes for namespace-related endpoints.
    /// </summary>
    public static class Namespaces
    {
        /// <summary>
        /// Base route for namespace operations.
        /// </summary>
        public const string Base = $"{VersionedBase}/namespaces";

        /// <summary>
        /// Route for a specific namespace by ID.
        /// </summary>
        public const string ById = $"{Base}/{{id:guid}}";

        /// <summary>
        /// Route for testing namespace connection.
        /// </summary>
        public const string TestConnection = $"{Base}/{{id:guid}}/test-connection";

        /// <summary>
        /// Route for getting queues in a namespace.
        /// </summary>
        public const string Queues = $"{Base}/{{id:guid}}/queues";

        /// <summary>
        /// Route for getting topics in a namespace.
        /// </summary>
        public const string Topics = $"{Base}/{{id:guid}}/topics";
    }

    /// <summary>
    /// Routes for queue-related endpoints.
    /// </summary>
    public static class Queues
    {
        /// <summary>
        /// Base route for queue operations.
        /// </summary>
        public const string Base = $"{VersionedBase}/namespaces/{{namespaceId:guid}}/queues";

        /// <summary>
        /// Route for a specific queue by name.
        /// </summary>
        public const string ByName = $"{Base}/{{queueName}}";

        /// <summary>
        /// Route for messages in a queue.
        /// </summary>
        public const string Messages = $"{Base}/{{queueName}}/messages";

        /// <summary>
        /// Route for peeking messages in a queue.
        /// </summary>
        public const string Peek = $"{Base}/{{queueName}}/messages/peek";

        /// <summary>
        /// Route for dead-letter messages in a queue.
        /// </summary>
        public const string DeadLetter = $"{Base}/{{queueName}}/dead-letter";

        /// <summary>
        /// Route for peeking dead-letter messages.
        /// </summary>
        public const string DeadLetterPeek = $"{Base}/{{queueName}}/dead-letter/peek";

        /// <summary>
        /// Route for listing scheduled messages in a queue.
        /// </summary>
        public const string Scheduled = $"{Base}/{{queueName}}/scheduled";

        /// <summary>
        /// Route for cancelling a specific scheduled message by sequence number.
        /// </summary>
        public const string ScheduledMessage = $"{Base}/{{queueName}}/scheduled/{{sequenceNumber:long}}";

        /// <summary>
        /// Route for cancelling a specific scheduled message by sequence number (alias for ScheduledMessage).
        /// </summary>
        public const string CancelScheduled = $"{Base}/{{queueName}}/scheduled/{{sequenceNumber:long}}";
    }

    /// <summary>
    /// Routes for topic-related endpoints.
    /// </summary>
    public static class Topics
    {
        /// <summary>
        /// Base route for topic operations.
        /// </summary>
        public const string Base = $"{VersionedBase}/namespaces/{{namespaceId:guid}}/topics";

        /// <summary>
        /// Route for a specific topic by name.
        /// </summary>
        public const string ByName = $"{Base}/{{topicName}}";

        /// <summary>
        /// Route for subscriptions in a topic.
        /// </summary>
        public const string Subscriptions = $"{Base}/{{topicName}}/subscriptions";
    }

    /// <summary>
    /// Routes for subscription-related endpoints.
    /// </summary>
    public static class Subscriptions
    {
        /// <summary>
        /// Base route for subscription operations.
        /// </summary>
        public const string Base = $"{Topics.Base}/{{topicName}}/subscriptions";

        /// <summary>
        /// Route for a specific subscription by name.
        /// </summary>
        public const string ByName = $"{Base}/{{subscriptionName}}";

        /// <summary>
        /// Route for messages in a subscription.
        /// </summary>
        public const string Messages = $"{Base}/{{subscriptionName}}/messages";

        /// <summary>
        /// Route for peeking messages in a subscription.
        /// </summary>
        public const string Peek = $"{Base}/{{subscriptionName}}/messages/peek";

        /// <summary>
        /// Route for dead-letter messages in a subscription.
        /// </summary>
        public const string DeadLetter = $"{Base}/{{subscriptionName}}/dead-letter";

        /// <summary>
        /// Route for peeking dead-letter messages.
        /// </summary>
        public const string DeadLetterPeek = $"{Base}/{{subscriptionName}}/dead-letter/peek";
    }

    /// <summary>
    /// Routes for message-related endpoints.
    /// </summary>
    public static class Messages
    {
        /// <summary>
        /// Base route for message operations.
        /// </summary>
        public const string Base = $"{VersionedBase}/messages";

        /// <summary>
        /// Route for a specific message by ID.
        /// </summary>
        public const string ById = $"{Base}/{{messageId}}";

        /// <summary>
        /// Route for sending a message.
        /// </summary>
        public const string Send = $"{Base}/send";

        /// <summary>
        /// Route for resending a dead-letter message.
        /// </summary>
        public const string Resend = $"{Base}/{{messageId}}/resend";
    }

    /// <summary>
    /// Routes for health check endpoints.
    /// </summary>
    public static class Health
    {
        /// <summary>
        /// Basic health check route.
        /// </summary>
        public const string Base = $"{VersionedBase}/health";

        /// <summary>
        /// Detailed health check route.
        /// </summary>
        public const string Ready = $"{Base}/ready";

        /// <summary>
        /// Liveness probe route.
        /// </summary>
        public const string Live = $"{Base}/live";
    }

    /// <summary>
    /// Routes for anomaly detection endpoints.
    /// </summary>
    public static class Anomalies
    {
        /// <summary>
        /// Base route for anomaly operations.
        /// </summary>
        public const string Base = $"{VersionedBase}/anomalies";

        /// <summary>
        /// Route for a specific anomaly by ID.
        /// </summary>
        public const string ById = $"{Base}/{{id:guid}}";

        /// <summary>
        /// Route for triggering anomaly detection.
        /// </summary>
        public const string Detect = $"{Base}/detect";
    }

    /// <summary>
    /// Routes for message-shape drift detection endpoints.
    /// </summary>
    public static class DriftFindings
    {
        /// <summary>
        /// Base route for drift finding operations.
        /// </summary>
        public const string Base = $"{VersionedBase}/drift-findings";

        /// <summary>
        /// Route for a specific drift finding by ID.
        /// </summary>
        public const string ById = $"{Base}/{{id:guid}}";

        /// <summary>
        /// Route for triggering drift detection.
        /// </summary>
        public const string Detect = $"{Base}/detect";

        /// <summary>
        /// Route for generating a producer-facing contract-violation export.
        /// </summary>
        public const string Export = $"{Base}/export";
    }

    /// <summary>
    /// Routes for proactive correlation endpoints (same-provider and cross-cloud).
    /// </summary>
    public static class CorrelationFindings
    {
        /// <summary>
        /// Base route for correlation finding operations.
        /// </summary>
        public const string Base = $"{VersionedBase}/correlation-findings";

        /// <summary>
        /// Route for a specific correlation finding by ID.
        /// </summary>
        public const string ById = $"{Base}/{{id:guid}}";

        /// <summary>
        /// Route for triggering correlation detection.
        /// </summary>
        public const string Detect = $"{Base}/detect";
    }

    /// <summary>
    /// Routes for external-signal ingestion and correlation (roadmap §5.D, C3 — M5, ADR-0008).
    /// </summary>
    public static class ExternalSignals
    {
        /// <summary>Base route for recording/listing external signals.</summary>
        public const string Base = $"{VersionedBase}/external-signals";

        /// <summary>Route for triggering external-signal correlation detection.</summary>
        public const string Detect = $"{Base}/detect";

        /// <summary>Route for a specific external-signal correlation by ID.</summary>
        public const string CorrelationById = $"{Base}/correlations/{{id:guid}}";
    }

    /// <summary>
    /// Routes for deterministic narration endpoints (roadmap §5.B, I4 — "Narrate").
    /// </summary>
    public static class Narrations
    {
        /// <summary>
        /// Base route for narration operations.
        /// </summary>
        public const string Base = $"{VersionedBase}/narrations";

        /// <summary>
        /// Route for a specific narration by ID.
        /// </summary>
        public const string ById = $"{Base}/{{id:guid}}";

        /// <summary>
        /// Route for triggering narration generation.
        /// </summary>
        public const string Generate = $"{Base}/generate";
    }

    /// <summary>
    /// Routes for predictive backlog forecasting endpoints (roadmap §5.E, P4 — "Predictive
    /// backlog signal").
    /// </summary>
    public static class BacklogForecasts
    {
        /// <summary>
        /// Base route for backlog forecast operations.
        /// </summary>
        public const string Base = $"{VersionedBase}/backlog-forecasts";

        /// <summary>
        /// Route for a specific backlog forecast by ID.
        /// </summary>
        public const string ById = $"{Base}/{{id:guid}}";

        /// <summary>
        /// Route for triggering backlog forecast computation.
        /// </summary>
        public const string Forecast = $"{Base}/forecast";
    }

    /// <summary>
    /// Routes for DLQ Intelligence endpoints.
    /// </summary>
    public static class Dlq
    {
        /// <summary>
        /// Base route for DLQ history operations.
        /// </summary>
        public const string Base = $"{VersionedBase}/dlq";

        /// <summary>
        /// Route for DLQ message history listing.
        /// </summary>
        public const string History = $"{Base}/history";

        /// <summary>
        /// Route for a specific DLQ message by ID.
        /// </summary>
        public const string ById = $"{History}/{{id:long}}";

        /// <summary>
        /// Route for the timeline of a specific DLQ message.
        /// </summary>
        public const string Timeline = $"{History}/{{id:long}}/timeline";

        /// <summary>
        /// Route for updating notes on a DLQ message.
        /// </summary>
        public const string Notes = $"{History}/{{id:long}}/notes";

        /// <summary>
        /// Route for triaging a DLQ message (status transition).
        /// </summary>
        public const string Status = $"{History}/{{id:long}}/status";

        /// <summary>
        /// Route for exporting DLQ messages.
        /// </summary>
        public const string Export = $"{Base}/export";

        /// <summary>
        /// Route for DLQ summary statistics.
        /// </summary>
        public const string Summary = $"{Base}/summary";

        /// <summary>
        /// Route for a namespace's DLQ error-cluster signatures. Namespace-scoped (route
        /// parameter named <c>namespaceId</c>, not <c>id</c>) so the ownership-enforcement
        /// action filter picks it up.
        /// </summary>
        public const string Signatures = $"{VersionedBase}/namespaces/{{namespaceId:guid}}/dlq/signatures";

        /// <summary>
        /// Route for a single failure signature's detail, by its stable hash.
        /// </summary>
        public const string SignatureById = $"{Signatures}/{{signatureHash}}";

        /// <summary>
        /// Route for a failure signature's merged, computed lifecycle timeline.
        /// </summary>
        public const string SignatureTimeline = $"{SignatureById}/timeline";

        /// <summary>
        /// Route for transitioning a failure signature's lifecycle status.
        /// </summary>
        public const string SignatureStatus = $"{SignatureById}/status";

        /// <summary>
        /// Route for creating or updating a failure signature's operational knowledge.
        /// </summary>
        public const string SignatureKnowledge = $"{SignatureById}/knowledge";

        /// <summary>
        /// Route for a failure signature's operational knowledge version history.
        /// </summary>
        public const string SignatureKnowledgeHistory = $"{SignatureKnowledge}/history";

        /// <summary>
        /// Route for marking a failure signature's knowledge as needing review.
        /// </summary>
        public const string SignatureKnowledgeReview = $"{SignatureKnowledge}/review";

        /// <summary>
        /// Route for dry-running a replay of a failure signature's member messages.
        /// </summary>
        public const string SignatureReplayPreview = $"{SignatureById}/replay/preview";

        /// <summary>
        /// Route for starting a replay of a failure signature's member messages.
        /// </summary>
        public const string SignatureReplay = $"{SignatureById}/replay";

        /// <summary>
        /// Route for listing a failure signature's past and in-flight replay jobs, most recent first.
        /// </summary>
        public const string SignatureReplayHistory = $"{SignatureReplay}/history";

        /// <summary>
        /// Route for finding this failure signature's occurrences in other namespaces in the
        /// fleet (Root Cause Explorer).
        /// </summary>
        public const string SignatureRootCauseMatches = $"{SignatureById}/root-cause-matches";

        /// <summary>
        /// Routes for auto-replay rule operations.
        /// </summary>
        public static class Rules
        {
            /// <summary>Base route for rule operations.</summary>
            public const string Base = $"{Dlq.Base}/rules";

            /// <summary>Route for a specific rule by ID.</summary>
            public const string ById = $"{Base}/{{id:long}}";

            /// <summary>Route for toggling a rule's enabled state.</summary>
            public const string Toggle = $"{Base}/{{id:long}}/toggle";

            /// <summary>Route for testing a rule against live messages.</summary>
            public const string Test = $"{Base}/test";

            /// <summary>Route for retrieving rule templates.</summary>
            public const string Templates = $"{Base}/templates";

            /// <summary>Route for rule execution statistics.</summary>
            public const string Stats = $"{Base}/{{id:long}}/stats";
        }
    }

    /// <summary>
    /// Routes for polling/cancelling an in-flight signature-replay job. Not namespace-scoped
    /// (like <see cref="Dlq.SignatureReplay"/>) — a job ID is already owner-scoped, matching the
    /// shape of the bulk-operations job routes.
    /// </summary>
    public static class SignatureReplayJobs
    {
        /// <summary>Base route for signature-replay job operations.</summary>
        public const string Base = $"{VersionedBase}/signature-replay-jobs";

        /// <summary>Route for a specific signature-replay job by ID.</summary>
        public const string ById = $"{Base}/{{id:guid}}";

        /// <summary>Route for cancelling a signature-replay job.</summary>
        public const string Cancel = $"{ById}/cancel";
    }

    /// <summary>Routes for the cross-namespace fleet operations overview.</summary>
    public static class Fleet
    {
        /// <summary>Base route for fleet operations.</summary>
        public const string Base = $"{VersionedBase}/fleet";

        /// <summary>Route for the fleet-wide DLQ overview.</summary>
        public const string Overview = $"{Base}/overview";
    }

    /// <summary>Routes for cross-cloud correlation tracing.</summary>
    public static class CrossCloudTrace
    {
        /// <summary>Base route for cross-cloud trace operations.</summary>
        public const string Base = $"{VersionedBase}/trace";

        /// <summary>Route for executing a trace lookup.</summary>
        public const string Trace = $"{Base}/{{traceId}}";
    }

    /// <summary>Routes for platform event streaming.</summary>
    public static class Events
    {
        /// <summary>Base route for event operations.</summary>
        public const string Base = $"{VersionedBase}/events";

        /// <summary>Route for the Server-Sent Events stream.</summary>
        public const string Stream = $"{Base}/stream";
    }

    /// <summary>Routes for the persistent audit trail.</summary>
    public static class Audit
    {
        /// <summary>Base route for audit log operations.</summary>
        public const string Base = $"{VersionedBase}/audit";

        /// <summary>Route for exporting audit logs as CSV or JSON.</summary>
        public const string Export = $"{Base}/export";

        /// <summary>Route for audit trail summary statistics.</summary>
        public const string Summary = $"{Base}/summary";
    }

    /// <summary>Routes for on-demand and scheduled backups (roadmap F2).</summary>
    public static class Backup
    {
        /// <summary>Base route for backup operations: POST to create, GET to list.</summary>
        public const string Base = $"{VersionedBase}/admin/backup";
    }

    /// <summary>Route for the caller-identity ("whoami") endpoint.</summary>
    public static class Me
    {
        /// <summary>Base route for the caller-identity endpoint.</summary>
        public const string Base = $"{VersionedBase}/me";
    }

    /// <summary>Routes for the Recovery Evidence Ledger.</summary>
    public static class Recovery
    {
        /// <summary>Base route for recovery operation queries.</summary>
        public const string Base = $"{VersionedBase}/recovery";

        /// <summary>Route for listing recovery operations.</summary>
        public const string Operations = $"{Base}/operations";

        /// <summary>Route for a specific recovery operation by ID.</summary>
        public const string OperationById = $"{Operations}/{{id:guid}}";

        /// <summary>Route for exporting one recovery operation's evidence.</summary>
        public const string OperationExport = $"{OperationById}/export";

        /// <summary>Route for verifying the caller's hash chain, from one operation's context.</summary>
        public const string OperationVerify = $"{OperationById}/verify";

        /// <summary>Route for listing recovery ledger entries.</summary>
        public const string Entries = $"{Base}/entries";

        /// <summary>Route for writing off a single recovery ledger entry.</summary>
        public const string EntryWriteOff = $"{Entries}/{{id:guid}}/write-off";

        /// <summary>Route for rehearsing the Eligibility Gate against one recovery ledger entry (roadmap §7 W1.2).</summary>
        public const string EntryRehearse = $"{Entries}/{{id:guid}}/rehearse";

        /// <summary>Route for the ageing report of open (non-terminal) entries.</summary>
        public const string Ageing = $"{Base}/ageing";

        /// <summary>Route for one signature's Evidence-Derived Trust Scoring report (roadmap §8.10).</summary>
        public const string Trust = $"{Base}/trust/{{signatureHash}}";

        /// <summary>Route for the Approval Queue — auto-replay rule matches escalated for manual review (roadmap §11 item 1).</summary>
        public const string ApprovalQueue = $"{Base}/approval-queue";

        /// <summary>Route for the fleet-wide autonomy dashboard (roadmap §11 item 5, §15 item 9).</summary>
        public const string AutonomyDashboard = $"{Base}/autonomy-dashboard";
    }

    /// <summary>Routes for the Playbook Ledger (M4 of the persistence wave, roadmap item 10).</summary>
    public static class Playbook
    {
        /// <summary>Base route for the Playbook Ledger.</summary>
        public const string Base = $"{VersionedBase}/playbook";

        /// <summary>Route for listing/creating Playbook Ledger entries.</summary>
        public const string Entries = $"{Base}/entries";

        /// <summary>Route for one Playbook Ledger entry by ID, plus its event chain.</summary>
        public const string EntryById = $"{Entries}/{{id:guid}}";

        /// <summary>Route for marking an entry under review.</summary>
        public const string EntryReview = $"{EntryById}/review";

        /// <summary>Route for a human's terminal disposition (approve/reject) of an entry.</summary>
        public const string EntryDisposition = $"{EntryById}/disposition";

        /// <summary>Route for verifying the caller's Playbook hash chain.</summary>
        public const string Verify = $"{Base}/verify";

        /// <summary>Route for the correlation accountability report (roadmap §5.D C4, §11 item 17).</summary>
        public const string CorrelationAccountability = $"{Base}/correlation-accountability";
    }

    /// <summary>Routes for P5 <c>PreventionRule</c> proposal/revocation (roadmap §5.C, staged
    /// Option B — <c>PREVENTION-RULE-DESIGN-2026-08-29.md</c>). Review/approve/reject of a
    /// proposed rule stays on the generic <see cref="Playbook"/> routes — this class only adds the
    /// two actions the generic Playbook Ledger surface can't express: proposing a
    /// rule-shaped payload from structured fields, and revoking an already-promoted one.</summary>
    public static class PreventionRules
    {
        /// <summary>Base route for P5 PreventionRule actions.</summary>
        public const string Base = $"{VersionedBase}/prevention-rules";

        /// <summary>Route for proposing a new rule, or a new version of an existing one.</summary>
        public const string Propose = Base;

        /// <summary>Route for the currently active (promoted) rules, optionally filtered by namespace.</summary>
        public const string Active = $"{Base}/active";

        /// <summary>Route for revoking a promoted rule.</summary>
        public const string Revoke = $"{Base}/{{id:guid}}/revoke";
    }

    /// <summary>Routes for Governance/RBAC grant management (M3 of the persistence wave, roadmap
    /// item 10's enforcement layer).</summary>
    public static class Governance
    {
        /// <summary>Base route for Governance grants.</summary>
        public const string Base = $"{VersionedBase}/governance";

        /// <summary>Route for listing/creating grants.</summary>
        public const string Grants = $"{Base}/grants";

        /// <summary>Route for revoking one grant by ID.</summary>
        public const string GrantRevoke = $"{Grants}/{{id:guid}}/revoke";
    }

    /// <summary>Routes for the Investigation Center — incident command for failure investigation.</summary>
    public static class FailureIntelligence
    {
        /// <summary>Base route for failure intelligence operations.</summary>
        public const string Base = $"{VersionedBase}/failure-intelligence";

        /// <summary>Route for the investigation center aggregated data.</summary>
        public const string InvestigationCenter = $"{Base}/investigation-center";
    }
}
