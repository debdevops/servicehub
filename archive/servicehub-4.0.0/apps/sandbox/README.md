# ServiceHub Sandbox (Experimental)

**Status: Experimental — not the supported product surface.** This is a standalone workspace app,
independent of both `apps/web` (the real ServiceHub SPA) and `apps/demo`. It is being built out
separately as an exploratory shell, not a finished feature.

## Purpose

An independent application shell for exploring ServiceHub concepts (namespaces, queues, topics,
subscriptions) without credentials, using fixture data via the shared `@servicehub/ui-shared`
package (`packages/servicehub-ui-shared`). Some of its pages are placeholders
(`ComingSoonPage.tsx`) rather than finished views — it is a scaffold for trying out ideas, not a
curated walkthrough the way `apps/demo` is.

## How to run

```bash
# From the repo root
./run.sh sandbox           # starts this app alone on http://localhost:5175
./run.sh all                 # starts ServiceHub + Web UI + Demo + Sandbox together

# Or directly via npm
npm run -w apps/sandbox dev  # http://localhost:5175
```

No cloud credentials, backend, or `.env` setup required.

## Limitations

- **No automated test coverage.** CI runs lint, typecheck (`tsc -b`), and build for this app so a
  broken build or type error is caught, but there is no test suite for its behavior. A green CI run
  here means it compiles and builds — nothing more.
- **Not covered by Playwright** or any other end-to-end suite.
- **Some pages are unfinished placeholders**, by design — this is where ServiceHub concepts get
  tried out, not where they're expected to already work.
- **Expect drift** against `packages/servicehub-ui-shared` as the main product evolves — this app
  and `apps/demo` are its least-exercised consumers.

## Stability

Experimental. Don't build product features against this app as if it were `apps/web`.
