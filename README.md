# ServiceHub

**A self-hosted tool for recovering stuck messages in Azure Service Bus, AWS SQS/SNS and GCP Pub/Sub — and proving what it did.**

Point it at your clouds and it shows what is dead-lettered and why, lets you put messages back with a preview first, and keeps a
tamper-evident record of every recovery. It runs as one process with one SQLite file. Message content never leaves your network: ServiceHub talks only
to your clouds and, if you add one, to a notification channel (Slack, Teams or a webhook), which gets queue names and
failure reasons, never message bodies.

> **Status:** this branch is **ServiceHub 4.1.0**, a from-scratch rewrite that is not released yet (`.version` still reads 4.0.0 until it is).
> The previous release lives, frozen, in [`archive/servicehub-4.0.0/`](archive/servicehub-4.0.0/). See the [changelog](CHANGELOG.md).

![Home: what needs you, how each cloud is doing, and the ServiceHub Agent](docs/screenshots/01-home.png)

*Screenshots are the built-in demo (`/demo/azure`): made-up data, nothing is sent anywhere.*

## What you get

**Simple** — Home and the work you do from it; everything else opens in place and lives in the URL, so a link shows someone exactly what you see.

- **Home** — what needs you, how every connected cloud is doing, and the ServiceHub Agent.
- **Dead letters · Active messages · Replayed · Auto Replay** (tabs on Home) — find a message, read why it failed, replay it, and watch whether it stayed fixed.

**Advanced** — four read-only investigation pages: **Overview**, **Recovery Ledger**, **Failure Signatures**, **Agents**.

![Dead letters](docs/screenshots/02-dead-letters.png)

## How it stays safe

- **A person decides by default.** The Agent replays on its own only for a failure it has earned trust on; otherwise it stops and asks. Every
  replay, human or automatic, goes through one gate that **fails closed** — a check that cannot run blocks the replay.
- **Honest about each cloud.** Azure can confirm a replayed message stayed out of the dead-letter queue. AWS and GCP cannot without a small
  DLQ observer, so their results read *"verification required"*, never *"verified"*.
- **Production namespaces are refused.** 4.1.0 has no production elevation; recovery there is denied for everyone.
- **Emergency stop.** Stops everything ServiceHub does on its own — rules and the Agent; a person can still replay deliberately.
  Switching it on needs a reason and the typed word `STOP`; lifting it, a reason and `LIFT`. Both are recorded in the ledger.
- **Evidence you can check offline.** Every recovery action is appended to a hash-chained ledger. Export it and verify it without ServiceHub:
  `python3 scripts/verify-recovery-chain.py <export.json>`. See [Recovery Evidence](docs/RECOVERY-EVIDENCE.md).
- **Credentials are encrypted at rest** (AES-GCM) and the key can be rotated: [Encryption key rotation](docs/ENCRYPTION-KEY-ROTATION.md).

## Quick start

**From source** (.NET 10 SDK, Node 22):

```bash
npm ci
./run.sh            # API on :5153, web on :3000 (proxied) — open http://localhost:3000
```

Open `/demo/azure` first to look around with made-up data, then **Add a cloud** (sidebar) to connect your own.

**Docker:**

```bash
export SERVICEHUB_ENCRYPTION_KEY="$(openssl rand -hex 32)"   # once — and keep it somewhere safe
docker compose up --build                                       # → http://localhost:8080 (this machine only)
```

ServiceHub **refuses to start in Production without an encryption key** — it protects every cloud connection string it stores. Losing the
key makes them unreadable, so back it up in a secret manager.

## Deploying it for real

| | |
|---|---|
| **One instance.** | One process, one SQLite file; a second instance on the same data directory exits. Keep the file on local block storage, not a network share. |
| **Data** | Set `ServiceHub__DataDirectory` (the Docker image uses the `/data` volume). |
| **Backups** | Settings → Backup, or `GET/POST /api/v1/admin/backup`. Restore takes effect on the next start: [Backup & restore](docs/BACKUP-RESTORE.md). |
| **Who is asking** | **ServiceHub does not log the browser in: anyone who can reach its port is the server's owner, an Administrator.** Keep it on `localhost` (the Docker Compose file binds `127.0.0.1` for this reason) or put it behind a private network or an authenticating reverse proxy before exposing it. What it *can* do is give other people and automation **limited** access: API keys (`Security:Authentication:ApiKeys`, sent as `X-API-KEY`), OIDC (`Security:Oidc:*`) or Azure Easy Auth (`Security:EasyAuth:*`) identify who acted, and roles granted in Settings (Viewer, Operator, Admin) limit what they may do. A wrong key is refused (`401`); no key means the owner. See [SECURITY.md](SECURITY.md). |
| **Cloud permissions** | Read/receive on the queues you want watched; send and delete only if you want to replay and purge. Prefer identity-based auth (managed identity, IAM role, workload identity) over long-lived secrets. |

## Documentation

| | |
|---|---|
| [Backup & restore](docs/BACKUP-RESTORE.md) | Take, verify and restore a backup |
| [Encryption key rotation](docs/ENCRYPTION-KEY-ROTATION.md) | Rotate the key that protects stored cloud credentials, and what to do if it leaks |
| [Recovery Evidence](docs/RECOVERY-EVIDENCE.md) | The hash-chained ledger and how an auditor verifies an export offline |
| [Tests](tests/README.md) · [Contributing](CONTRIBUTING.md) · [Security](SECURITY.md) · [Changelog](CHANGELOG.md) | |

## Contributing

```bash
./runtest.sh --all     # everything CI runs: lint, type-check, backend + frontend suites with 60 % floors, browser tests, guards
```

See [CONTRIBUTING.md](CONTRIBUTING.md). Licensed under the [MIT License](LICENSE).
