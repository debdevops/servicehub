# Demo Mode

ServiceHub's zero-credential surface is the hosted `/demo/*` routes — fully client-side, no
backend at all.

`/demo/azure`, `/demo/aws`, and `/demo/gcp` (`apps/web/src/router.tsx`) render the exact same
pages as the real app (Messages, Dashboard, Fleet, DLQ Intelligence, Cross-Cloud Trace, Audit,
...), wrapped in `DemoModeProvider` (`apps/web/src/lib/demo/DemoContext.tsx`). That provider makes
every data hook short-circuit and return fixture data instead of calling the API — there is no
backend involved at all. This is what you're looking at if you're browsing a hosted deployment of
the ServiceHub UI without having connected a namespace: a fully synthetic walkthrough of the
product, safe to run anywhere (a public web host included), because there's no real API behind it.

To run the real API and UI together against your own cloud credentials, use `docker compose up
--build` or `./run.sh` — see the root [README](../README.md#quick-start) and
[self-hosting/README.md](../self-hosting/README.md).

## What's synthetic, and what's accurate

Demo Mode's data is synthetic — generated messages, generated DLQ entries, generated audit
history. None of it reflects a real workload.

What *is* accurate: Demo Mode capability labels reflect each provider's real, current capability
matrix (`docs/PROVIDER-SUPPORT.md`), not aspirational future-phase language. A preview provider's
card states plainly that it's implemented and unit-tested, not validated against live cloud
services, capability-gated, and carries no parity guarantee with Azure — rather than the earlier,
now-corrected copy that implied full live browsing was future work ("Phase 2"). Demo Mode has
always been available for AWS/GCP; what changed is that the wording next to it now says what's
actually true today instead of describing a roadmap.

## Enabling real AWS/GCP browsing

Demo Mode works identically regardless of the `CloudProviders:Aws:Enabled` /
`CloudProviders:Gcp:Enabled` flags. Live browsing against a real AWS or GCP account requires an
operator to explicitly enable the relevant flag on the server — see `docs/CONFIGURATION.md`.
