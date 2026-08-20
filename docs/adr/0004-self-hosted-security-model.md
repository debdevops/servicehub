# ADR-0004: Self-hosted only — no multi-tenant SaaS mode

**Status:** Accepted

## Context

Message queue contents are frequently sensitive: payment payloads, PII, internal system state,
order details, credentials that shouldn't be there but sometimes are. A tool built to make dead
letters maximally *visible* — full body inspection, search, AI pattern clustering — is, by
construction, a tool with access to that sensitive content. The most common commercial path for a
product like this is a hosted, multi-tenant SaaS offering: easier onboarding, centralized updates,
no customer infrastructure required.

## Decision

ServiceHub ships as self-hosted, single-instance software for one team, deployed inside the
operator's own network, using the operator's own cloud credentials, writing to the operator's own
disk. There is no hosted ServiceHub product and no code path where message content or credentials
leave the deployment's own process boundary. This holds for every capability in the product,
including the AI pattern detection, which runs as client-side heuristics in the browser rather than
calling an external AI API — the same "your data never leaves your environment" claim applies
uniformly, not just to message storage.

Supporting security posture, all deny-by-default: authentication is on (`Security:Authentication:Enabled`
defaults `true`, with an empty key list rather than a shipped default), AWS/GCP provider flags
default off, destructive AWS DLQ scanning defaults off, AI is opt-in and local-only by construction
(not by flag), the Docker Quick Start binds to loopback only, and production namespaces are
read-only regardless of authentication state — replay, send, and bulk operations are disabled
entirely against them, not merely hidden in the UI.

## Alternatives considered

- **Multi-tenant SaaS.** Rejected: this would mean customer message content and cloud credentials
  crossing into a vendor-operated (ServiceHub-operated) environment — a fundamentally different
  trust model than "your data never leaves your network," which is the actual reason a team
  handling sensitive queue contents would trust this tool with peek access in the first place.
  Building toward this would require walking back the self-hosted claim, not extending it.
- **A hosted control plane with customer data staying local ("bring your own storage" SaaS).**
  Considered as a middle ground; rejected for now as solving a problem — centralized fleet
  management across many self-hosted instances — that no named customer has asked for, and that
  would still require a control-plane trust boundary this project doesn't currently need to defend.
- **Optional telemetry/AI calls to an external service, off by default.** The AI capability boundary
  goes further than "off by default": the pattern-detection engine is architecturally client-side
  and heuristic, not a toggle on an otherwise-server-side AI call — see
  [ADR-0005](0005-ai-capability-boundary.md).

## Consequences

- No cross-team or cross-tenant sharing beyond per-owner API-key/OIDC scoping within a single
  deployment — a team wanting shared visibility runs one ServiceHub instance and issues each member
  scoped credentials, rather than getting per-tenant isolation from a shared instance.
- No built-in horizontal scaling or high availability (see [ADR-0003](0003-single-instance-sqlite.md)) —
  a direct consequence of "one team, one process, one trust boundary."
- The product's entire security story is falsifiable by reading the open-source code, which is the
  intended trust mechanism: the in-app Security & Privacy page links every claim (what's encrypted,
  what's redacted, what's never stored) directly to the code that implements it, rather than asking
  an operator to trust a vendor's SOC 2 report for infrastructure they can't inspect.
- This constrains future feature work: any proposal that requires message content or credentials to
  leave the deployment's own process — a hosted dashboard, a cloud-side AI call, cross-instance
  telemetry aggregation with payload content — needs to be evaluated against this ADR first, not
  treated as a routine feature addition.
