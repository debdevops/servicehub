# ServiceHub Reasoning Companion

An optional, **self-hosted, disabled-by-default** service that reads
structured, already-aggregated incident evidence and proposes plain-language
observations for a human to review — the roadmap's Tier 3 "reasoning
companion" (`SERVICEHUB-AUTONOMOUS-MASTER-ROADMAP-2026-08-27.md` §7, W5). It
is not a third-party or cloud AI API — it is a plain FastAPI container that
runs on your own machine, on the same private Docker network as the main
ServiceHub container, under your own control, exactly like `services/ai`.

## What it does, and does not do

- `POST /propose` takes a batch of `EvidenceRecord`s (counts, lifecycle
  status, normalised error terms — never a message's raw content) and, if a
  local Ollama instance is configured, asks it to produce short advisory
  observations. `GET /health` reports whether a reasoning backend is
  configured at all.
- Every proposal is plain-language text for a human reviewer. This service
  has no access to `IRecoveryLedger`, no access to any message broker, and no
  way to execute, approve, or promote anything. On the ServiceHub side, the
  only thing anything it produces can ever become is one more `Proposed`
  entry in the Playbook Ledger, disposed of by a human exactly like every
  other entry there — see `AIBoundaryArchitectureTests` (extended to cover
  this service's .NET-side client) for the enforced version of that
  invariant.
- It never calls an external or cloud LLM API. `OLLAMA_HOST` is the only
  backend it knows how to talk to, and that is expected to be a same-host or
  same-network Ollama instance the operator runs themselves. Leaving
  `OLLAMA_HOST` unset is the default and fully supported posture: `/propose`
  then always returns an empty list with `method: "disabled"`, and ServiceHub
  works identically without this container present.

## Why it's opt-in

Set `ReasoningAgent:Enabled=true` and `ReasoningAgent:ServiceUrl` in
ServiceHub's configuration to use it (see
`services/api/src/ServiceHub.Api/appsettings.json` — `Enabled: false` by
default). Separately, set this container's own `OLLAMA_HOST` env var to point
at a local Ollama instance — without it, the service runs, answers `/health`,
and always returns no proposals. Both switches must be on for anything to
happen; either one being off is a fully supported, permanent configuration,
not a bootstrap state.

An operator opting into an *external* LLM API (rather than a local Ollama
instance) is a real amendment to ADR-0004 and is explicitly out of scope for
this container — see the roadmap's item 23.

## How to run it

```bash
cd services/agent
python -m venv .venv && source .venv/bin/activate
pip install -r requirements-dev.txt
uvicorn app.main:app --reload --port 8010
```

Run the tests:

```bash
pytest
```
