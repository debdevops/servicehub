# ServiceHub Simulator Mode

The Simulator starts three synthetic namespaces (Azure, AWS, GCP) and seeds them with realistic
messages — no cloud credentials or connection strings required. Use it for demos, onboarding, and
testing forensic rules.

---

## Quick Start

```bash
./run.sh --simulator
```

Open `http://localhost:3000` and navigate to **Simulator** in the sidebar.

> **Manual startup (advanced):** If you need to start services separately:
> ```bash
> # Terminal 1 — Start API in Simulator mode (port 5200)
> ASPNETCORE_ENVIRONMENT=Simulator dotnet run \
>   --project services/api/src/ServiceHub.Api/ServiceHub.Api.csproj \
>   --no-launch-profile \
>   --urls http://localhost:5200
>
> # Terminal 2 — Start UI (port 3000)
> cd apps/web && npm run dev
> ```
>
> **Why `--no-launch-profile`?**  
> `launchSettings.json` sets `ASPNETCORE_ENVIRONMENT=Development` and overrides `--environment`.
> Omitting the launch profile lets the shell environment variable take effect.

---

## Seeded Data

| Namespace | Provider | Queues | Topics | Active Messages | DLQ Messages |
|-----------|----------|--------|--------|-----------------|--------------|
| `sim-azure-prod` | Azure Service Bus | 4 | 2 | 50 | 10 |
| `sim-aws-prod` | AWS SQS/SNS | 4 | 2 | 50 | 10 |
| `sim-gcp-prod` | GCP Pub/Sub | 4 | 2 | 50 | 10 |

Each namespace contains the following entities:

| Entity | Type | Messages | DLQ |
|--------|------|----------|-----|
| `orders` | Queue | 20 | 5 |
| `payments` | Queue | 15 | 3 |
| `notifications` | Queue | 10 | 2 |
| `audit-log` | Queue | 5 | 0 |
| `checkout` | Topic | 30 | 6 |
| `fulfillment` | Topic | 20 | 4 |

---

## What Triggers Forensic Rules

The seeded data is designed to exercise all four forensic categories:

| Category | Trigger | Entities |
|----------|---------|---------|
| `MaxDeliveryCountExceeded` | `DeliveryCount >= 10` | `orders`, `payments` |
| `TTLExpiredException` | `EnqueuedTimeUtc` older than 7 days | `audit-log` |
| `ApplicationError` | `DeadLetterReason = "Application"` | `checkout`, `fulfillment` |
| `NetworkTimeout` | `DeadLetterErrorDescription` contains "timeout" | `notifications` |

---

## Simulator Control Panel

Navigate to `/simulator` in the UI to access the control panel:

| Section | What it does |
|---------|-------------|
| **Provider Status** | Shows live message/DLQ counts per cloud provider |
| **Fault Injection** | Simulates failures: MaxDelivery, VisibilityExpiry, AckDeadlineStorm, KmsError, OrderingStall, NetworkTimeout |
| **Time Control** | Advance simulated UTC clock by 5 min / 1 hour / 24 hours — triggers TTL and visibility window expiry |
| **DLQ Flood** | Inject 1–50 realistic DLQ messages into any entity for dashboard testing |
| **Reset & Reseed** | Wipe all state and reseed to the default dataset |

---

## Running Tests Against Simulator

Frontend tests mock all API calls and do not require the API to be running.

```bash
# Unit + component tests
cd apps/web && npm run test -- --run

# Backend unit tests
cd services/api && dotnet test tests/ServiceHub.UnitTests

# Backend integration tests (use default test environment, not Simulator)
cd services/api && dotnet test tests/ServiceHub.IntegrationTests
```

---

## Simulator API Endpoints

All endpoints are under `/api/v1/simulator/` and require `ASPNETCORE_ENVIRONMENT=Simulator`.
In Development or Production mode these routes return `404`.

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/simulator/status` | Namespaces + active faults + simulated time |
| `POST` | `/simulator/faults` | Inject a fault |
| `DELETE` | `/simulator/faults` | Clear all active faults |
| `POST` | `/simulator/reset` | Wipe and reseed all data |
| `POST` | `/simulator/advance-time` | Advance simulated UTC clock |
| `POST` | `/simulator/inject-dlq-flood` | Inject DLQ messages |

Interactive API docs: `http://localhost:5200/scalar/v1`
