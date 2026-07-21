# Load & smoke tests (k6)

A lightweight [k6](https://k6.io) harness that exercises ServiceHub's read paths under
concurrency and asserts latency thresholds — the evidence behind the "sub-second forensic
visibility over thousands of messages" claim. It runs against **Simulator mode**, so it needs
no cloud credentials and is safe to run anywhere.

This harness is intentionally **not wired into CI** (load characteristics are host-dependent);
it is a foundation you run locally or in a dedicated performance environment.

## Prerequisites

- [k6 installed](https://grafana.com/docs/k6/latest/set-up/install-k6/) (`brew install k6`)
- ServiceHub running in Simulator mode:

  ```bash
  ./run.sh --simulator          # API on http://localhost:5200
  # or
  docker compose up --build     # API on http://localhost:8080
  ```

## Run

```bash
# Defaults to http://localhost:5200
k6 run tests/load/peek-messages.js

# Point at another base URL (e.g. the Docker image)
k6 run -e BASE_URL=http://localhost:8080 tests/load/peek-messages.js
```

## What it does

- Discovers the seeded simulator namespaces and their queues/subscriptions.
- Hammers the message-peek and DLQ endpoints with a ramping virtual-user load.
- Asserts thresholds: p95 latency and a <1% error rate. The run **fails** if they are breached,
  so it doubles as a performance regression gate you can adopt when ready.

## Thresholds (tune in the script)

| Metric | Threshold |
|---|---|
| `http_req_duration` p95 | < 800 ms |
| `http_req_failed` rate | < 1% |
| `checks` rate | > 99% |
