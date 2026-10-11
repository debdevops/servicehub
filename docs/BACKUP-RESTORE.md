# Backup & Restore

> **In this article:** what a ServiceHub backup is, how to take one, how to check it before you rely on it, and how to restore from it.
>
> **Who this is for:** whoever keeps a self-hosted ServiceHub instance safe — a platform engineer, an administrator, or a solo operator.
> No SQLite knowledge is needed.

Everything ServiceHub remembers — cloud connections (encrypted), dead-letter history, the audit trail, the recovery ledger, rules — lives in **one
SQLite file**, `servicehub.db`, in the data directory (`ServiceHub__DataDirectory`; `/data` in the Docker image). A backup is a consistent copy of
that file, taken while ServiceHub is running, with a manifest that lets you prove the copy is the one you took.

**Two things a backup does not contain:**
- **The encryption key.** Only a fingerprint of it. Keep the key somewhere else (a secret manager) — a backup that carried its own key would hand
  every saved cloud connection to anyone who got the file. A backup restored on a server with a different key opens, but its saved connections do not.
- **Anything outside the database**, such as your configuration and environment variables.

Backup and restore are **admin-only** and instance-wide (Settings → Backup, or the API below).

## 1. Take a backup

**In the app:** Settings → Backup → *Take a backup now*. **Or by API:**

```bash
curl -X POST http://localhost:8080/api/v1/admin/backup -H "X-ServiceHub-Intent: create-backup"
```

(The examples use the Docker port, `8080`; with `./run.sh` the API is on `5153`.) The `X-ServiceHub-Intent` header is required for anything that changes state; without it the API answers `428 intent_required` and says which header to send.
If authentication is on (README → *Deploying it for real*), add your `X-API-KEY` too. The response is the manifest — a real one:

```json
{
  "backupId": "20260929-164649Z",
  "createdAtUtc": "2026-09-29T16:46:49.854615+00:00",
  "serviceHubVersion": "4.2.0+726f8fcc1b84979ce3ff244254bf12c375ac3878",
  "sqlite": { "fileName": "servicehub-dlq.db", "sizeBytes": 299008, "sha256": "a3c734c2…7802cc7" },
  "integrityCheck": "ok",
  "encryptionKeyFingerprint": "sha256:e2e86eb22f771121"
}
```

Each backup is one folder, `backups/<backupId>/`, under the data directory (or `Backup:BackupDirectory`), holding `manifest.json` and the snapshot,
`servicehub-dlq.db`. The snapshot is taken with SQLite's `VACUUM INTO`, so it is consistent as of one instant even under load. The manifest records:

- **`sqlite.sha256`** — the checksum of the snapshot, so a copy that was changed or damaged later is caught.
- **`integrityCheck`** — SQLite's integrity check, run on the snapshot right after it is taken. Anything other than `"ok"` and the backup is **discarded
  automatically** — a broken backup is never left on disk looking like a good one.
- **`encryptionKeyFingerprint`** — a one-way fingerprint of the key that was active. Compare it before restoring anywhere else.

(A real manifest also carries `namespaceStore` — always `null` since 4.1.0 — and a `consistencyNote` that still mentions a "namespace JSON store". That wording is carried
over from 4.0.0, which kept connections in a second file; 4.1.0 keeps everything in the one database, so there is nothing to be out of step with.)

**Scheduled:** off by default. Set `Backup:ScheduledBackupIntervalHours` (env `Backup__ScheduledBackupIntervalHours`) above 0 and restart. `Backup:RetentionCount`
(default 14) keeps that many of the newest backups and deletes older ones after each successful backup.

**List and download:** `GET /api/v1/admin/backup` lists them (newest first) and any restore waiting for the next start; `GET /api/v1/admin/backup/<id>/download`
(or the download button) saves the database file — **keep a copy somewhere other than this disk.** ServiceHub does not ship backups off the machine.

## 2. Check a backup before you rely on it

In the app, *Restore…* on a backup runs the checks and lists them before it offers anything; by API, `GET /api/v1/admin/backup/<id>/check`. Checking changes nothing, and answers `canRestore` with five checks:

| Check | Fails when |
|---|---|
| The file is the one that was backed up | its checksum no longer matches the manifest |
| The database inside is sound | SQLite's integrity check finds a problem |
| It was made with this server's encryption key | the fingerprint differs — its saved connections could not be opened here |
| Its schema is one this version knows | it was made by a **newer** ServiceHub (older is fine — missing steps are applied at start) |
| Its evidence ledger verifies | the recovery ledger's hash chain is broken — a restore would bring back evidence that cannot be trusted |

## 3. Restore

Restore **stages** a backup and applies it at the **next start**; ServiceHub never swaps a database out from under itself while running.

1. Settings → Backup → *Restore…* on the backup, read the checks, type `RESTORE`, then *Restore at the next start*. Or by API (the check runs again and a failing backup is refused with `409` and the failed checks):
   ```bash
   curl -X POST http://localhost:8080/api/v1/admin/backup/20260929-164649Z/restore \
     -H "X-ServiceHub-Intent: restore-backup" -H "Content-Type: application/json" -d '{"confirm":"RESTORE"}'
   ```
   A restore waiting for the next start shows in the list (`pending`) and can be withdrawn: *Cancel the restore*, or `DELETE /api/v1/admin/backup/pending` (same intent header).
2. **Restart ServiceHub.** At start it moves the current database aside as `servicehub.db.before-restore-<timestamp>` (with its `-wal`/`-shm` files) and puts
   the backup in place. **Everything recorded since that backup is replaced** — keep the moved-aside file until you are sure.
3. Check `/health/ready`, that your namespaces are there, and that the Recovery Ledger's chain still verifies (`GET /api/v1/recovery/chain` → `isValid: true`).

**Restoring onto a new server, or when ServiceHub cannot start:** stop it, copy the downloaded `servicehub-dlq.db` over `<data directory>/servicehub.db`, delete any
`servicehub.db-wal` and `servicehub.db-shm` beside it, and start. Do the encryption-key comparison by hand first (manifest fingerprint vs. the server's; the
Settings → *Access & security* panel shows the current one) — this path skips the app's checks.

## 4. What this does not do

- No off-host shipping (S3, blob storage) — moving backups elsewhere is yours to arrange.
- No continuous or point-in-time recovery — backups are periodic snapshots.
- No restore of anything that is not in the database, and no key backup — see the top.
