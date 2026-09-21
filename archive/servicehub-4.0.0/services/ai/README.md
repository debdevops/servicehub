# ServiceHub AI Service

An optional, **self-hosted, disabled-by-default** companion service that provides
higher-quality DLQ failure-signature clustering than ServiceHub's built-in
deterministic clustering. It is not a third-party or cloud AI API — it is a
plain FastAPI container that runs on your own machine, on the same private
Docker network as the main ServiceHub container, under your own control. See
[`docs/ARCHITECTURE.md` §6a](../../docs/ARCHITECTURE.md#6a-the-ai-capability-boundary)
for how this fits ServiceHub's "no external AI API calls" position.

## What it does

- `POST /analyze` groups a batch of DLQ `FeatureRecord`s by error signature using
  TF-IDF + DBSCAN over normalised error text (see `app/clustering.py`) — no LLM,
  no embeddings, no training data, no cold start.
- `GET /health` reports readiness; the .NET side polls this to decide whether to
  use this service at all.

## Why it's opt-in

Set `AI:Enabled=true` and `AI:ServiceUrl` in ServiceHub's configuration to use it
(see `services/api/src/ServiceHub.Api/appsettings.json` — `Enabled: false` by
default). On the .NET side, `DlqSignatureAnalysisService` tries this service
first and transparently falls back to `DeterministicClusteringStrategy` — a
purely local, in-process .NET heuristic — whenever this service is disabled,
unreachable, unhealthy, or returns a malformed response. **ServiceHub works
identically without this container present**; it is a quality-of-clustering
upgrade, never a dependency.

## How to run it

Standalone, for local development:

```bash
cd services/ai
python -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt
uvicorn app.main:app --reload
```

Via Docker Compose (as part of the full stack — built and started by default,
but inert unless `AI:Enabled=true`):

```bash
docker compose up --build
```

The service has no host-published port — it is only reachable from other
containers on the compose network, e.g. `http://servicehub-ai:8000/health`.

## Tests

```bash
cd services/ai
pip install -r requirements-dev.txt
pytest
```

## What this does NOT do

- **No external calls.** This service makes no outbound network calls of its
  own — clustering is local, in-process scikit-learn (TF-IDF + DBSCAN), not a
  call to any hosted model or API.
- **No message bodies, ever.** The request model (`FeatureRecord` in
  `app/models.py`) only accepts pre-extracted structured features (sizes,
  hashes, categorical labels) — there is no field capable of carrying a
  message body or payload content. The .NET side (`MessageFeatures.cs`,
  `MessageFeatureRecord.cs`) is the source of truth for this shape.
- **No authentication.** This service is not designed to be reachable from
  anywhere except the internal Docker Compose network. It must never be
  published to the host or any external network.
- **No persistence.** No database connection of any kind — ServiceHub's .NET
  side owns all persistence.
- **No execution authority.** This service cannot replay, purge, or otherwise
  mutate anything — it only returns cluster groupings. `AIBoundaryArchitectureTests`
  (in `ServiceHub.UnitTests`) enforces this on the .NET side: no AI-adjacent
  type may ever call a mutating `IRecoveryLedger`/`IMessageOperationsService`
  member.
- **No dependency on the main ServiceHub container's health**, nor vice versa:
  ServiceHub starts and serves normally whether this service is present,
  absent, or unhealthy.
