# ServiceHub RC1 — Release Notes

This document is a curated, reader-facing summary of this release. For the exhaustive
engineering-level log of every change, see `CHANGELOG.md`.

## Security fixes

We found and fixed a cross-tenant read on two endpoints. Stating that plainly, without
minimizing it, because publishing it clearly is the only credible way to handle it.

- **Cross-tenant read in the Cloud Bridge API** — `GET /api/v1/cloud-bridge/namespaces/{id}/entities`
  and `GET /api/v1/cloud-bridge/namespaces/{id}/visibility/{queueName}` performed no ownership
  check: any authenticated caller who knew or guessed another tenant's namespace ID could list its
  entities or read its visibility status. Both endpoints now verify the caller owns the namespace
  before returning data, and return `404` (not `403`) on a mismatch, so a caller can't distinguish
  "not yours" from "doesn't exist."
- **Cross-owner read/write in DLQ Intelligence** — `GetByIdAsync`, `GetTimelineAsync`,
  `UpdateNotesAsync`, and `GetSummaryAsync` took no owner parameter, so any authenticated caller who
  guessed or enumerated a DLQ message ID could read or annotate another tenant's message. All four
  now require and filter on `ownerId`, matching the isolation the list endpoint already had.
- **Rate-limiting bypass behind a reverse proxy** — the rate limiter keyed solely on the TCP
  connection's remote IP, which is the proxy's IP for every request when ServiceHub runs behind one
  (e.g. Azure App Service) — collapsing every tenant into a single shared limit bucket. It now keys
  on the authenticated owner ID when available, falling back to remote IP only for unauthenticated
  requests.

## Corrections

**We are correcting a previous claim about the SPA token.** Earlier documentation implied that
enabling the SPA token would block automated clients (curl, scanners) from reaching the API without
loading the UI first. That implication was not accurate, and we're saying so directly rather than
quietly editing it away:

> Previously implied: *enabling the SPA token means any HTTP client without a browser is blocked.*
>
> Corrected: the SPA token is a CSRF and casual-automation mitigation, **not an authentication
> boundary**. It is obtainable by anyone who can fetch the index page —
> `curl https://your-instance/ | grep spaToken` retrieves it exactly as a browser would, because
> it's static content in the response, not something requiring JavaScript execution. It is **not**
> proof a human is driving a real browser, and it is **not per-user identity** — every request
> carrying a valid token, browser or `curl`, is treated as the same shared built-in admin owner.

This is by design for single-operator self-hosting (see `self-hosting/security-hardening/README.md`
for the full trust model and how to move to per-user identity via OIDC or Easy Auth). Publishing
this correction — rather than leaving the earlier, more reassuring wording in place — is itself the
strongest signal we can give that this project's security statements can be trusted: when we find
one that overstated a guarantee, we say so.

## Data-safety fix: AWS DLQ background monitoring is now opt-in

Background DLQ scanning previously polled every namespace on the same timer regardless of
provider, including AWS. SQS has no non-destructive peek — every scan was a real `ReceiveMessage`
call that increments each message's `ReceiveCount`, which can push a message past its queue's
`maxReceiveCount` and dead-letter it by accident, purely as a side effect of ServiceHub looking at
it. Background scanning (and the manual "Scan Now" trigger) now skip AWS namespaces by default;
`DlqMonitor:AllowDestructivePeek:Aws` (default `false`) lets an operator opt back in once they
accept that consequence. This is framed as a data-safety fix, not a feature removal: the previous
behavior could silently alter the very data an operator was trying to inspect.

## Accuracy fix: Demo Mode capability copy

AWS/GCP preview cards in Demo Mode previously said "Phase 2 provider... full live browsing ships
in Phase 2," implying live browsing was future work. It has been available all along, gated behind
an operator-enabled flag. The copy now states what's actually true: implemented and unit-tested,
not validated against live cloud services, capability-gated, no parity guarantee with Azure — an
accuracy correction, not a new capability. See `docs/DEMO-MODE.md` and `docs/PROVIDER-SUPPORT.md`.

## Backend Simulator has been removed

The `ASPNETCORE_ENVIRONMENT=Simulator` backend and `docker-compose.yml`'s Simulator profile have
been permanently removed. The zero-credential way to explore the UI is now the client-side Demo
Mode (`/demo/azure`, `/demo/aws`, `/demo/gcp`) — fully functional, backend-free, and safe to
access from anywhere without needing real cloud credentials. See `docs/DEMO-MODE.md`.

## Also in this release

Restrictive CSP is now correctly applied to Staging environments (previously the permissive
development CSP policy could apply to any non-Production environment name by mistake); repeated
API-key authentication failures are now throttled (429 after a threshold, closing an unthrottled
brute-force gap). See `CHANGELOG.md` for the full list, including provider-aware DLQ forensic
classification, namespace sharing, RBAC roles, OIDC Bearer authentication, Live Tail, Bulk
Operations, and Slack/Teams-native webhook alerts.

## Upgrading

See `docs/MIGRATION-NOTES.md` — no schema change and no data migration is required for this
release.
