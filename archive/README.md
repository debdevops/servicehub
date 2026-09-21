# Archive — ServiceHub 4.0.0

> ## ⛔ FROZEN — do not edit anything under `archive/`
>
> This is the complete ServiceHub **4.0.0** codebase, preserved exactly as it was released
> ([ADR-0012](../docs/adr/0012-single-servicehub-application.md),
> [ADR-0013](../docs/adr/0013-archive-the-entire-4-0-0-codebase.md)). It is **reference material**.
> Nothing in it is changed — not for a lint rule, not for a dependency bump, not for a "quick fix".
> If it genuinely must change, that is a new, recorded decision.

## What is here

`servicehub-4.0.0/` **is the old repository root**, unchanged in structure. Every relative path
inside it resolves exactly as it did before the move, which is why it still builds and runs
without a single edit:

```
archive/servicehub-4.0.0/
├── apps/
│   ├── web/                  React 19 SPA — the 33-page 4.0.0 interface
│   ├── demo/  sandbox/       experimental standalone apps
├── services/
│   ├── api/                  .NET 10 API — the engine: 33 controllers, 20 background workers,
│   │                         the autonomy engine, the Recovery Evidence Ledger
│   ├── ai/  agent/           optional Python services
├── packages/
│   └── servicehub-ui-shared/ 29 API modules + 35 hooks
├── scripts/                  evidence-chain verifiers, conformance suite, key generation
├── Dockerfile  docker-compose.yml  .dockerignore  .env.example
├── run.sh  run.ps1  runtest.sh
├── package.json  package-lock.json  .version
```

Repository-level material — `docs/`, `.github/`, `README.md`, `CHANGELOG.md`, `LICENSE`,
`CONTRIBUTING.md` — stays at the repository root.

## Building and running it

Everything works from inside `servicehub-4.0.0/`, exactly as it did from the old repo root:

```bash
cd archive/servicehub-4.0.0

./run.sh                    # API + web UI from source
docker compose up --build   # the container image (needs a .env — see .env.example)
./runtest.sh --quick        # backend unit tests + frontend tests
```

CI (`.github/workflows/`) builds and tests this tree on every push, and the release image is
built from it.

The released 4.0.0 is also tagged **`v4.0.0`**.

## Reading it and reusing it

**Read it freely.** It holds years of live-verification fixes — error handling, retries, edge
cases, exact endpoint sequences — and that is paid-for knowledge.

**Reuse by copying, not by importing.** Copy a function, a component or a test into the new code
when it earns its place. Nothing outside `archive/` imports from it, and nothing inside it is
ever changed to accommodate the new code.

## Version

`servicehub-4.0.0/.version` is `4.0.0` and is read by the archive's own builds. The repository's
root `.version` is a separate file and is not part of the archive.
