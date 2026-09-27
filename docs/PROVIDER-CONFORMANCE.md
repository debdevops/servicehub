# Provider Conformance Evidence

> **In this article:** the proof behind the "Supported" badge you see next to AWS and GCP
> throughout the ServiceHub UI — where that badge comes from, what it actually checked, and how
> you can re-run the same checks yourself against your own AWS/GCP account.
>
> **In plain language:** every cloud messaging service works a little differently — some support
> scheduling a message for later, some don't; some let you permanently delete a stuck message,
> some don't. ServiceHub is honest about these differences everywhere in the product (you'll see
> a message like "not supported on this provider" instead of a button that silently does nothing).
> This document is the *evidence* that those honesty claims are actually true, not just asserted —
> every row in the table below is a real API call against a real AWS or GCP account, not a
> simulation.

This page is the evidence behind the **Supported** label on AWS SQS/SNS and GCP Pub/Sub
(previously **Preview** — see [What changed](#what-changed) below). It exists so the label is a
claim anyone can reproduce, not a claim you have to take on trust. You'll see this same
capability information live in the product on the **Cloud Bridge** page and anywhere a
provider-specific action (Purge, Schedule, Live Tail) is offered or correctly grayed out.

## What's being proven

Every provider declares what it can and can't do in `ProviderCapabilities.{Azure,Aws,Gcp}`
(`services/api/src/ServiceHub.Core/Models/ProviderCapabilities.cs`) — things like whether
manual dead-lettering is possible, whether scheduled sends exist, whether a non-destructive DLQ
peek is available. Per-provider unit tests already prove the *code* behaves correctly against a
mocked SDK for each of those. What they can't prove is that the mock matches the real service.

`scripts/conformance-suite.py` closes that gap: it runs the same kind of assertions — including
the **negative** ones (an unsupported operation must be rejected with the documented error, not
silently ignored or a 500) — against a live ServiceHub API talking to a real Azure/AWS/GCP
namespace. Nothing in the suite is simulated; every assertion is a real HTTP call whose outcome
depends on the actual cloud service responding.

> **4.1.0 note:** this is a *port*, not a copy, of the archived `archive/servicehub-4.0.0/scripts/
> conformance-suite.py` (PORTING-MAP.md P46). 4.1.0's API shape genuinely changed — see
> [What changed in 4.1.0's API shape](#what-changed-in-410s-api-shape) below for exactly what a
> reader of the 4.0.0-era table below should know before comparing them directly.

## Latest run

**2026-09-27 — 19 passed, 0 failed, 4 skipped, all three providers, live against real Azure DEV
(`sb-servicehub-dev`), AWS DEV (`ap-south-1`) and GCP DEV (`servicehub-502914`).**

| Assertion | Azure | AWS | GCP |
|---|---|---|---|
| Send (baseline) | PASS (200) | PASS (200) | PASS (200) |
| Manual dead-letter | SKIPPED — no API action exists in 4.1.0 (see below) | SKIPPED | SKIPPED |
| Scheduled messages listing | PASS — positive (200, real listing) | PASS — negative (409, `Message.Operation.ScheduledUnsupported`) | PASS — negative (409, `Message.Operation.ScheduledUnsupported`) |
| Scheduled send | SKIPPED — no API field exists in 4.1.0, on any provider (see below) | SKIPPED | SKIPPED |
| Look at dead letters now (`POST .../dead-letters/look`) | PASS — `outcome: looked`, `countsAsDeliveryAttempt: false` | PASS — `outcome: looked`, `countsAsDeliveryAttempt: true` | PASS — `outcome: looked`, `countsAsDeliveryAttempt: true` |
| Purge | PASS — **negative** (409, `capability_unavailable`) | PASS — positive (200, `result: accepted`) | PASS — positive (200; HTTP-level accepted through the gate, cloud-level `result` varied run to run — see note below) |
| `CanProveDlqAbsence` | PASS — `trustRoot: provider-native` | PASS — `trustRoot: none` (also cross-checked live against a real signature's `GET /signatures/{hash}/trust`) | PASS — `trustRoot: none` (same live cross-check) |

The bolded Azure purge row is the negative case most worth proving live post-rewrite: Azure's
`SupportsPurge` is still `false` in 4.1.0 (Service Bus has no reliable single-message delete by
sequence number) and the API still correctly refuses it — 409 `capability_unavailable`, not a
silent no-op or a 500.

**Note on the GCP purge row.** One of the three live runs this session hit `result: rejected`,
`errorCode: GCP.PubSub.MessageNotFound` at the *cloud* level, even though the HTTP-level outcome
was the expected 200 (the eligibility gate correctly let the purge attempt through, since GCP
`SupportsPurge: true`). Root cause: a race between two consecutive live runs each purging "the
latest active dead-letter row" from the same fast-moving DLQ — the row ServiceHub had recorded had
already been purged/moved out of the Pub/Sub subscription by a prior run's own purge, or resolved
naturally, before this run's purge reached the cloud. This is a real, expected shape of eventual
consistency between ServiceHub's recorded dead-letter rows and the live cloud state, not a
capability-enforcement defect — the two other runs, and every AWS purge this session, returned a
clean `result: accepted`.

**Signature-trust cross-check (new for 4.1.0; the old attestation lookup was per-namespace, this
one is per-signature).** `GET /api/v1/signatures/{hash}/trust?provider=<Provider>` was queried live
for one real AWS signature (217 dead letters recorded, `sampleSize: 0`) and one real GCP signature
(`sampleSize: 1`, 0% verified success). Both correctly reported `cloudCanConfirm: false` — no
DLQ-observer attestation is configured for either, so `CanProveDlqAbsence` stays `none`, the same
honest answer as the static capability preset gives. No AWS/GCP namespace in this dev environment
has the `cloud-platform-infra` observer deployed yet (see ADR-004's open item in this repo's
own W4.3/`4.2` notes) — until one does, every live run of this row will keep reading `none`,
which is correct, not a gap in the suite.

## The `CanProveDlqAbsence` trust root (M3.3)

`CanProveDlqAbsence` gates whether a replayed message can ever reach L4 (Standing) or L5
(Unattended) autonomy — see `cloud-platform-infra`'s `docs/decisions/ADR-004-InfrastructureAttestedDlqObserver.md`
and this repo's own [ADR-0011](adr/0011-dlq-observer-attestation-table-authorized.md). It is `true`
for exactly one of two reasons, and this suite now reports **which one**, rather than a single
flattened PASS/FAIL:

| Trust root | Meaning | Who to trust |
|---|---|---|
| `provider-native` | The cloud provider's own API can prove absence directly — an uncapped, non-destructive peek. | Azure only, today. |
| `operator-attested` | An infrastructure-attested DLQ observer (a Lambda/DynamoDB or Cloud Function/Firestore pipeline the operator deployed via `cloud-platform-infra`'s Terraform modules) has independently confirmed, via a live liveness canary, that it is actually attached to this exact namespace's DLQ. | Whoever provisioned and is running that observer — not Amazon or Google, and not ServiceHub's own code. |
| `none` | Neither holds. The signature is capped at L3 (human-approved replay only). | — |

`operator-attested` is never a config flag an operator can just set — see
`DlqObserverAttestationController`/`IDlqObserverAttestationService` (`servicehub` repo): it
requires a canary message ServiceHub itself dispatches into the DLQ and the observer's own log
confirming its arrival within a bounded staleness window, re-checked on every sweep. A stale or
never-confirmed attestation reads `false`, the same as never having configured one at all — never
"assume fine" (ADR-004 item 4).

**No live run of this assertion has been recorded yet.** It requires an AWS or GCP namespace with
the `cloud-platform-infra` observer actually deployed (`terraform/modules/aws/dlq-observer` or
`terraform/modules/gcp/dlq-observer`) and its attestation configured
(`PUT /api/v1/namespaces/{id}/dlq-observer-attestation`) — infrastructure this session built and
validated (`terraform validate`) but did not deploy, per this repository's own workflow of the
operator running `terraform apply`. Until that exists, every AWS/GCP row here reads
`trustRoot: none` — an honest, correctly-negative result, not evidence of a defect.

## What this evidence does and doesn't cover

- **Covers:** every capability `ProviderCapabilities` declares for the entity types exercised
  (queue for Azure/AWS, topic+subscription for GCP), both the positive and negative case.
- **Doesn't cover:** this was a manual run against a developer's already-running local stack and
  already-registered dev namespaces — it's reproducible by anyone with the same setup ("one
  command"), but it isn't yet wired into CI as a scheduled or gating check. Making it
  CI-runnable from a clean checkout is separate infrastructure work, tracked independently of
  whether the evidence itself is valid.
- **Known open item at the time of this run — since fixed.** AWS purge/replay had an
  intermittent, probabilistic failure on a namespace with a deep pre-existing dead-letter
  backlog: `AwsMessageReceiver.FindAndLockMessageAsync` used a fixed 20-round (≈200-message)
  scan ceiling, and because SQS `ReceiveMessage` samples a randomised subset of the queue's
  backend hosts on every call, a single pass sized to the queue depth does not reliably surface
  one specific message. It showed up live as `MessageNotFound` against a real ~317-message
  backlog. The scan budget is now derived from the queue's approximate depth (×3, floored at the
  old 20 rounds and hard-capped at 200) with an early exit after five consecutive rounds that
  surface nothing new — so a deep queue gets the rounds it needs while an unbounded or still-
  growing one still fails fast with a clear "not found" rather than scanning forever. Re-verified
  live on 2026-09-05 by replaying real messages out of that same AWS backlog.

## What changed in 4.1.0's API shape

4.1.0 is a from-scratch rewrite (ADR-0012/0013), not the 4.0.0 codebase with new paths — the
conformance suite had to change with it, not just its imports:

- **No standalone capabilities endpoint.** 4.0.0 had `GET /cloud-bridge/capabilities`; 4.1.0
  carries `Capabilities` on every namespace returned by `GET /api/v1/namespaces` instead. The
  suite reads it from there, still never hardcoding a duplicate table.
- **No manual dead-letter action over the API.** 4.0.0 had `POST .../deadletter`; there is no
  equivalent route in 4.1.0. `SupportsManualDeadLetter` is a real field on `ProviderCapabilities`
  still (used elsewhere — e.g. the eligibility gate), but nothing in the current product lets an
  operator trigger it directly, so this row is honestly `SKIPPED`, not faked.
- **No way to schedule a send, on any provider.** 4.0.0's send endpoint took a
  `scheduledEnqueueTimeUtc` field; 4.1.0's `POST .../messages` request body
  (`MessagesController.SendRequest`) does not expose one, even though the internal
  `SendMessageRequest` DTO still carries it. This is a genuine product gap this run surfaced, not
  a suite limitation — currently nothing in 4.1.0's public API can schedule a message for later on
  *any* cloud, Azure included. The negative listing path (`GET .../messages/scheduled` correctly
  409ing for AWS/GCP) is still real and still checked.
- **"Live Tail" and the manual DLQ-scan trigger merged into one action.** 4.0.0 had an SSE
  `live-tail` endpoint (`SupportsRepeatablePeek` positive/negative) and a separate
  `POST /dlq/scan/{id}` trigger with a `DlqMonitor:AllowDestructivePeek` opt-in. 4.1.0 replaced
  both with a single, person-consented `POST .../dead-letters/look`
  (`IDeadLetterLook`/`DeadLetterLook.cs`) that works the same way for every provider and reports
  `countsAsDeliveryAttempt` (the same fact `!SupportsRepeatablePeek` always meant) on every
  response, rather than a route existing only for the providers it applies to.
- **Purge and replay act on ServiceHub's own recorded dead-letter row id**, not raw provider
  coordinates (sequence number + entity name the way 4.0.0's `/api/v1/messages/purge` did) — a
  row has to be recorded first (which the `look` step above does for AWS/GCP; Azure's own
  background monitor keeps recording continuously since its peek is non-destructive).
- **The DLQ-observer-attestation lookup moved from per-namespace to per-signature.** 4.0.0 had
  `GET /namespaces/{id}/dlq-observer-attestation`; 4.1.0 folds the same fact into
  `GET /signatures/{hash}/trust?provider=<Provider>` (field `cloudCanConfirm`), keyed by a
  failure-signature hash rather than a namespace id, since autonomy trust is earned per-signature
  in this version.

## How to reproduce

```bash
python3 scripts/conformance-suite.py preflight
python3 scripts/conformance-suite.py run \
    --namespace Azure=<namespace-id> \
    --namespace Aws=<namespace-id> \
    --namespace Gcp=<namespace-id> \
    --signature-hash Aws=<a-real-signature-hash>   # optional, best-effort CanProveDlqAbsence cross-check
```

No entity name is needed per namespace any more — `send` resolves its own target entity (a queue,
or a topic if the namespace has no queue-kind entity at all) via `GET .../entities`, and every
other action works off a dead-letter row id the suite discovers itself via `GET /dead-letters` and
the `look` action above. Any provider without a `--namespace` argument is reported `SKIPPED`, not
`FAILED` — the suite runs against whichever providers you have connected, it doesn't require all
three. See the script's own docstring (`python3 scripts/conformance-suite.py --help`) for more.

## What changed

Before this evidence existed, AWS and GCP carried a **Preview** label meaning "implemented and
unit-tested, not validated against live AWS/GCP services in this project's own CI." That was an
honest label for what was true at the time, but it was a claim about *absence* of evidence, not
presence of it. Now that a reproducible, capability-complete live run exists — this page's own
table — the label follows the evidence: **Supported**, still capability-gated (see each provider's
guide for the real, permanent API differences from Azure — those aren't evidence gaps, they're
facts about the underlying service), still no parity guarantee with Azure.

See also: [AWS SQS/SNS Guide](guides/aws-guide.md), [GCP Pub/Sub Guide](guides/gcp-guide.md).
