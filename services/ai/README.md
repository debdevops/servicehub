# ServiceHub AI Service

A skeleton FastAPI service — packaging and health-contract scaffolding for the
clustering/anomaly-detection algorithm landing in a future milestone (P0-5).
No algorithm lives here yet.

## What it is

- A stateless FastAPI app with two endpoints: `GET /health` and `POST /analyze`.
- `POST /analyze` currently returns a stub response with the correct shape
  (`clusters: []`, `explanation: null`) — the real clustering logic will be
  implemented against this same contract.
- Designed to run as an internal-only sidecar in Docker Compose, alongside the
  main ServiceHub API container (see the repo-root `docker-compose.yml`).
- Each `FeatureRecord` in a request carries a caller-supplied `ref` (opaque
  string) that is round-tripped in the response (`representative_ref`,
  `first_occurrence_ref`, `last_occurrence_ref`, and each cluster/singleton
  member's `ref`) instead of a positional list index. This service treats
  `ref` as opaque — it never interprets, dereferences, or persists it. Refs
  must be present and unique within a request; missing or duplicate refs are
  rejected with `422`.

## How to run it

Standalone, for local development:

```bash
cd services/ai
python -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt
uvicorn app.main:app --reload
```

Via Docker Compose (as part of the full stack):

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

- **No algorithm.** `/analyze` is a stub; the real clustering/anomaly model
  lands in a later milestone against this same request/response contract.
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
- **No dependency on the main ServiceHub container's health**, nor vice
  versa: ServiceHub starts and serves normally whether this service is
  present, absent, or unhealthy.
