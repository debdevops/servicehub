# ServiceHub Demo (Experimental)

**Status: Experimental — not the supported product surface.** This is a standalone workspace app,
separate from `apps/web` (the real ServiceHub SPA) and separate from `apps/web`'s own in-app Demo
Mode routes (`/demo/azure`, `/demo/aws`, `/demo/gcp`), which are the ones the root
[README](../../README.md#quick-start) documents and Playwright covers in CI.

## Purpose

A curated, no-credentials tour: browse the Dashboard, Messages, and DLQ pages against fixture data
to see how ServiceHub surfaces incidents across Azure, AWS, and GCP, without connecting a real
namespace. It consumes the same fixtures and demo-data plumbing as `apps/web`'s in-app Demo Mode,
via the shared `@servicehub/ui-shared` package (`packages/servicehub-ui-shared`), but runs as an
independent app shell rather than routes inside the main SPA.

## How to run

```bash
# From the repo root
./run.sh demo              # starts this app alone on http://localhost:5174
./run.sh all                # starts ServiceHub + Web UI + Demo + Sandbox together

# Or directly via npm
npm run -w apps/demo dev    # http://localhost:5174
```

No cloud credentials, backend, or `.env` setup required — everything here is fixture data served
client-side.

## Limitations

- **No automated test coverage.** CI runs lint, typecheck (`tsc -b`), and build for this app to
  catch a broken build or a type error introduced by a change elsewhere in the monorepo, but there
  is no test suite exercising its actual behavior. Don't treat a green CI run here as proof this app
  works correctly — only that it compiles and builds.
- **Not covered by Playwright.** The e2e suite (`apps/web/e2e/`) exercises `apps/web`'s in-app Demo
  Mode, not this app.
- **Expect drift.** As `packages/servicehub-ui-shared` evolves for the main product, this app is
  the least-exercised consumer of it — a breaking change here is more likely to go unnoticed
  between releases than the same change in `apps/web`.

## Stability

Experimental. Don't build product features against this app as if it were `apps/web` — if a
capability needs to ship for real users, it belongs in `apps/web`'s own Demo Mode routes, which
carry the test coverage and documentation this app doesn't.
