# Demo Mode

ServiceHub has three distinct "no real cloud account needed" surfaces. They look similar but are
not the same thing — this page exists so you can tell which one you're looking at.

> [!WARNING]
> Surfaces 2 and 3 below (Simulator mode) disable authentication, rate limiting, and connection
> string encryption **entirely, by design**. This is not a bug or an oversight to be hardened
> later — it's the intended shape of a zero-credential local demo. Simulator mode must never be
> reachable from a network. `docker-compose.yml` enforces this by binding the demo port to
> `127.0.0.1` (loopback) only.

## 1. Hosted `/demo/*` routes — fully client-side, no backend at all

`/demo/azure`, `/demo/aws`, and `/demo/gcp` (`apps/web/src/router.tsx`) render the exact same
pages as the real app (Messages, Dashboard, Fleet, DLQ Intelligence, Cross-Cloud Trace, Audit,
...), wrapped in `DemoModeProvider` (`apps/web/src/lib/demo/DemoContext.tsx`). That provider makes
every data hook short-circuit and return fixture data instead of calling the API — there is no
backend involved at all. This is what you're looking at if you're browsing a hosted deployment of
the ServiceHub UI without having connected a namespace: a fully synthetic walkthrough of the
product, safe to run anywhere (a public web host included), because there's no real API behind it.

## 2. Simulator mode via Docker Compose

```bash
docker compose up --build            # → http://localhost:8080
```

This runs the real API and UI together, with `ASPNETCORE_ENVIRONMENT=Simulator`. Unlike surface 1,
there **is** a real backend here — it's just seeded with synthetic namespaces instead of talking to
actual Azure/AWS/GCP accounts. `docker-compose.yml` binds the port to `127.0.0.1:8080` only; change
it to `0.0.0.0:8080:8080` only for a deliberate, understood LAN exposure of a *real* (non-Simulator)
deployment — never as a shortcut for the Simulator default.

## 3. Local Simulator profile

```bash
./run.sh --simulator
```

Same Simulator backend as surface 2, run natively instead of in a container: API on `:5200`, UI on
`:3000`, no build step. Seeds three synthetic namespaces (Azure, AWS, GCP), each with realistic
active and dead-lettered messages. Useful for local development and testing forensic rules without
credentials. See `SIMULATOR.md` for seeded-data details and manual startup steps.

## What's synthetic, and what's accurate

All data behind all three surfaces is synthetic — generated messages, generated DLQ entries,
generated audit history. None of it reflects a real workload.

What *is* accurate: Demo Mode capability labels reflect each provider's real, current capability
matrix (`docs/PROVIDER-SUPPORT.md`), not aspirational future-phase language. A preview provider's
card states plainly that it's implemented and unit-tested, not validated against live cloud
services, capability-gated, and carries no parity guarantee with Azure — rather than the earlier,
now-corrected copy that implied full live browsing was future work ("Phase 2"). Demo Mode has
always been available for AWS/GCP; what changed is that the wording next to it now says what's
actually true today instead of describing a roadmap.

## Enabling real (non-Simulator) AWS/GCP browsing

Demo Mode and Simulator mode work identically regardless of the `CloudProviders:Aws:Enabled` /
`CloudProviders:Gcp:Enabled` flags (Simulator registers all three providers unconditionally). Live
browsing against a real AWS or GCP account requires an operator to explicitly enable the relevant
flag on the server — see `docs/CONFIGURATION.md`.
