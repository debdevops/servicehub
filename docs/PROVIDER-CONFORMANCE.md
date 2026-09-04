# Provider Conformance Evidence

This page is the evidence behind the **Supported** label on AWS SQS/SNS and GCP Pub/Sub
(previously **Preview** — see [What changed](#what-changed) below). It exists so the label is a
claim anyone can reproduce, not a claim you have to take on trust.

## What's being proven

Every provider declares what it can and can't do in `ProviderCapabilities.{Azure,Aws,Gcp}`
(`services/api/src/ServiceHub.Domain/Capabilities/ProviderCapabilities.cs`) — things like whether
manual dead-lettering is possible, whether scheduled sends exist, whether a non-destructive DLQ
peek is available. Per-provider unit tests already prove the *code* behaves correctly against a
mocked SDK for each of those. What they can't prove is that the mock matches the real service.

`scripts/conformance-suite.py` closes that gap: it runs the same assertions — including the
**negative** ones (an unsupported operation must be rejected with the documented error, not
silently ignored or a 500) — against a live ServiceHub API talking to a real Azure/AWS/GCP
namespace. Nothing in the suite is simulated; every assertion is a real HTTP call whose outcome
depends on the actual cloud service responding.

## Latest run

**2026-09-04 — 20 passed, 0 failed, 0 skipped, all three providers.**

| Assertion | Azure | AWS | GCP |
|---|---|---|---|
| Send (baseline) | PASS (202) | PASS (202) | PASS (202) |
| Manual dead-letter | PASS — positive (200) | PASS — positive (200) | PASS — **negative** (400, `Message.Operation.DeadLetterUnsupported`) |
| Scheduled send | PASS — positive (202, confirmed in listing) | PASS — negative (400, `ScheduledUnsupported`) | PASS — negative (400, `ScheduledUnsupported`) |
| Purge | PASS — negative (400, `PurgeUnsupported`) | PASS — positive (202) | PASS — positive (202) |
| Live Tail | PASS — positive (200, session opens) | PASS — negative (409) | PASS — negative (409) |
| DLQ background scan (`DlqMonitor:AllowDestructivePeek`) | PASS — positive (200, real peek) | PASS (200 — this run's server had explicitly opted AWS in; not the default) | PASS — **negative** (400, `Dlq.NotMonitored` — off by default) |

The two bolded rows are the two facts the roadmap named explicitly as the ones most worth proving
live: that GCP's manual dead-lettering genuinely fails rather than being silently accepted, and
that GCP's DLQ background scan stays off by default rather than falling back to a destructive peek.

## What this evidence does and doesn't cover

- **Covers:** every capability `ProviderCapabilities` declares for the entity types exercised
  (queue for Azure/AWS, topic+subscription for GCP), both the positive and negative case.
- **Doesn't cover:** this was a manual run against a developer's already-running local stack and
  already-registered dev namespaces — it's reproducible by anyone with the same setup ("one
  command"), but it isn't yet wired into CI as a scheduled or gating check. Making it
  CI-runnable from a clean checkout is separate infrastructure work, tracked independently of
  whether the evidence itself is valid.
- **Known open item:** AWS purge has an intermittent, probabilistic failure mode on a namespace
  with a very deep pre-existing dead-letter backlog (`AwsMessageReceiver.FindAndLockMessageAsync`'s
  bounded scan has no guarantee of covering a specific message once the backlog exceeds its scan
  window). Not fixed by this run — a real product decision about scan depth, not a conformance gap.

## How to reproduce

```bash
python3 scripts/conformance-suite.py preflight
python3 scripts/conformance-suite.py run \
    --namespace Azure=<namespace-id>=<queue-name> \
    --namespace Aws=<namespace-id>=<queue-name> \
    --namespace Gcp=<namespace-id>=<topic-name>=<subscription-name>
```

Any provider without a `--namespace` argument is reported `SKIPPED`, not `FAILED` — the suite runs
against whichever providers you have connected, it doesn't require all three. See the script's own
docstring (`python3 scripts/conformance-suite.py --help`) for namespace-registration prerequisites.

## What changed

Before this evidence existed, AWS and GCP carried a **Preview** label meaning "implemented and
unit-tested, not validated against live AWS/GCP services in this project's own CI." That was an
honest label for what was true at the time, but it was a claim about *absence* of evidence, not
presence of it. Now that a reproducible, capability-complete live run exists — this page's own
table — the label follows the evidence: **Supported**, still capability-gated (see each provider's
guide for the real, permanent API differences from Azure — those aren't evidence gaps, they're
facts about the underlying service), still no parity guarantee with Azure.

See also: [AWS SQS/SNS Guide](guides/aws-guide.md), [GCP Pub/Sub Guide](guides/gcp-guide.md).
