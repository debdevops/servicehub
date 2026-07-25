# Known Limitations

This page collects every deliberate constraint and architectural trade-off in ServiceHub in one
place, instead of scattering caveats across a dozen other documents. Each entry states what the
limitation is, why it exists, and whether it's expected to change. Nothing here is a bug waiting
to be found — these are known, and most are deliberate.

## Single instance only (SQLite + in-process event bus)
The DLQ/audit database is SQLite (file-based, single-writer) and the platform event bus
(`InProcessPlatformEventBus`) is an in-memory `Channel<T>` inside one process. Running two
instances against the same data directory is not supported — there is no shared-state
coordination between them. **Not planned to change** in the current architecture: horizontal
scaling would require a shared database (e.g. PostgreSQL) and a distributed event bus, both out
of scope for this release.

## Namespaces stored in a JSON file, not the database
Namespace connection records (`servicehub-namespaces.json`) predate the SQLite DLQ database and
were never migrated into it. New fields are added additively (older files rehydrate with defaults
for missing fields), which has kept this workable without a migration so far. **Not changing in
this release** — unifying namespaces into the SQLite store is the planned direction, not a
committed timeline; see `services/api/ARCHITECTURE.md` §15.

## Schema upgrades applied by hand at startup
There is no EF Core Migrations pipeline. Schema evolution (e.g. the `DlqMessage.CloudProvider`
column defaulting to `Azure` for pre-existing rows, or the `ENC:V2:` → `ENC[v1]` connection-string
re-encryption) is handled by ad hoc, hand-written startup code, not a migration framework.
**Not changing in this release** — EF Core Migrations is the planned next step for schema
evolution, not a committed timeline; see `services/api/ARCHITECTURE.md` §16.

## No key rotation — rotating the key makes stored connections unreadable
`Security:EncryptionKey` derives the AES-GCM key via HKDF/PBKDF2. There is no rotate-and-re-encrypt
tool: changing the key after connections have been saved makes every previously stored connection
string permanently undecryptable, requiring those namespaces to be re-entered. **Not currently
planned** — treat the encryption key as fixed for the lifetime of a deployment's stored data.

## All browser sessions share one owner identity and one rate-limit budget
With no per-user authentication configured, every browser session is the same built-in `__spa__`
owner — one namespace/DLQ/audit scope and one rate-limit bucket for every visitor. **This is
already fixable today, not a future roadmap item**: enabling `Security:Oidc:*` (any standards-
compliant OIDC identity provider) gives each authenticated user their own isolated owner ID
(`oidc:{sub}`); see `docs/CONFIGURATION.md`.

## AWS SQS peek is destructive, so DLQ monitoring is opt-in
SQS has no non-destructive peek — every read is a real `ReceiveMessage` call that increments the
message's `ReceiveCount`, which can push a message past its queue's `maxReceiveCount` and
dead-letter it by accident. Background DLQ scanning therefore skips AWS namespaces by default;
`DlqMonitor:AllowDestructivePeek:Aws` (default `false`) lets an operator opt back in once they
accept that consequence. **Not something ServiceHub can fix** — it's an SQS platform constraint,
not a gap in this implementation; the opt-in flag is the permanent shape of the mitigation, not a
stopgap pending a future fix.

## GCP has no message-count API
Pub/Sub has no endpoint that reports a live message count. ServiceHub reports `0` rather than
fabricating a number (`ProviderCapabilities.Gcp.SupportsMessageCounts = false`). **Not planned to
change** — this is a GCP platform limitation.

## Azure has no single-message delete
The Azure Service Bus SDK has no reliable way to delete one message by sequence number. Purge is
disabled for Azure namespaces rather than approximated with something less reliable
(`ProviderCapabilities.Azure.SupportsPurge = false`). **Not planned to change** — this is an Azure
SDK limitation, not a ServiceHub gap.

## Forwarded headers trusted from any source, so audit `ClientIp` is not tamper-evident
`Program.cs` clears `ForwardedHeadersOptions.KnownProxies`/`KnownIPNetworks` so `X-Forwarded-For`
is accepted from any immediate connection (needed because Azure App Service's front-end IPs
aren't fixed in advance). That means `HttpContext.Connection.RemoteIpAddress` — which audit
logging reads — has already been overwritten by whatever the client claims, before any
application code runs. The `ClientIp` column in the audit trail should be read as
operator-supplied, not as cryptographic proof of origin, unless ServiceHub is deployed behind a
proxy that strips/overwrites client-supplied `X-Forwarded-For` before it reaches the app.
**Fixable per-deployment today** by restricting `KnownProxies`/`KnownIPNetworks` to your actual
front-end proxy's address(es); not changed by default because the correct value is
deployment-specific.

## Shared-IP auth throttling behind a proxy
`AuthFailureThrottle` keys on `HttpContext.Connection.RemoteIpAddress` specifically to avoid
trusting an easily-spoofed header — but because of the same forwarded-headers configuration above,
that value is already the client-supplied `X-Forwarded-For` behind most reverse proxies. In that
deployment shape, every user behind the same proxy (or a load balancer, or a NAT) shares one
lockout bucket, and an attacker can rotate the header value to dodge the throttle entirely.
**Same fix as above** — restrict `KnownProxies`/`KnownIPNetworks` to your real proxy for this
protection to hold.

## Redaction adds per-log-line CPU cost
`LogRedactor.SanitiseForLog()` runs on every log line that carries user-controlled content, to
strip connection strings, API keys, and tokens before they reach the log sink. This is a
deliberate CPU-for-safety trade-off, not an oversight. **Not planned to change** — the cost is
accepted intentionally; removing it would reintroduce a `cs/log-forging`-class secret-leak risk
that CI's CodeQL gate exists specifically to catch.
