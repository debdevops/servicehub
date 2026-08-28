# Backup & Restore

ServiceHub's persistent state lives in two independent stores: a SQLite database (DLQ
intelligence, audit trail, recovery evidence ledger, and everything else EF Core owns) and a JSON
file holding the namespace/connection registry. This document describes how ServiceHub backs both
up, what a backup bundle contains, and how an operator restores from one. Restore is a **manual,
operator-driven procedure** — there is no automated restore endpoint, by design (see §5).

## 1. What gets backed up

Each backup run produces one timestamped **bundle directory** (e.g. `20260828-153000Z/`) under the
configured backup directory, containing:

| File | Contents |
|---|---|
| `servicehub-dlq.db` | A consistent snapshot of the SQLite database, taken via `VACUUM INTO`. |
| `servicehub-namespaces.json` | A copy of the namespace JSON store, if one exists yet. Omitted on a fresh instance with no namespaces. |
| `manifest.json` | Metadata about the bundle — see §2. |

## 2. The manifest

`manifest.json` records, for each file: its name, size, and a SHA-256 checksum. It also records:

- **`integrityCheck`** — the result of `PRAGMA integrity_check` run against the SQLite snapshot
  immediately after it was taken. A backup whose snapshot fails this check is discarded
  automatically and the backup operation reports failure — a broken bundle is never left on disk
  looking like a good one.
- **`encryptionKeyFingerprint`** — a non-reversible fingerprint (`sha256:<16 hex chars>`) of the
  connection-string encryption key that was active when the backup was taken. **The key itself is
  never included anywhere in a backup bundle.** Before restoring, compare this fingerprint against
  the fingerprint of the environment you're restoring into (see §4, step 2) — if they don't match,
  the restored namespace JSON's encrypted connection strings will not decrypt, and every namespace
  will need to be re-added with its plaintext connection string.
- **`consistencyNote`** — a reminder of the model described in §3, embedded in the bundle itself so
  it travels with the backup.

## 3. Consistency model — read this before you restore

**The SQLite snapshot and the namespace JSON copy are captured independently. They are not a
single atomic transaction across both stores.**

- The SQLite snapshot has a well-defined consistent point: the instant `VACUUM INTO` completes.
  Everything in `servicehub-dlq.db` reflects the database as of that instant.
- The namespace JSON file is copied as a single atomic file operation (ServiceHub's namespace
  repository always writes via a temp-file-then-rename sequence, so a copy never observes a
  partial write) — but at a separate, nearby instant, not synchronized with the SQLite snapshot.

In practice this means: if a namespace was added or edited in the few milliseconds between the two
captures, the restored SQLite database and the restored namespace store could disagree about that
one namespace (e.g. a `DlqMessage` row referencing a `NamespaceId` the restored namespace file
doesn't yet contain, or vice versa). This is a narrow, cosmetic inconsistency — ServiceHub's UI and
API tolerate a `NamespaceId` with no matching namespace (it just won't resolve a display name) —
not a correctness or data-loss issue. If your operational tolerance for even this narrow window is
zero, pause namespace create/edit/delete activity for the few seconds a backup takes.

## 4. Manual restore procedure

Restore is deliberately manual and fail-safe: nothing is overwritten until you've verified the
bundle you're restoring is the one you intend, and every step below is easy to abort before the
point of no return (step 5).

1. **Stop the ServiceHub instance.** Restoring into a running instance risks corrupting whatever
   the live process is mid-write on.

2. **Verify the bundle before touching anything.**
   - Recompute the SHA-256 of `servicehub-dlq.db` and (if present) `servicehub-namespaces.json`
     and compare against `manifest.json`'s `sqlite.sha256` / `namespaceStore.sha256`.
     ```bash
     shasum -a 256 servicehub-dlq.db servicehub-namespaces.json
     ```
   - Confirm `manifest.json`'s `integrityCheck` reads `"ok"`. If it doesn't, do not use this
     bundle — pick an earlier one.
   - Confirm `manifest.json`'s `encryptionKeyFingerprint` matches the fingerprint of the
     environment you're restoring into. You can read the current environment's fingerprint from
     any backup taken on it, or take a fresh (discardable) backup on the target environment via
     `POST /api/v1/admin/backup` and compare fingerprints. A mismatch means the restored
     namespace store's encrypted connection strings will fail to decrypt after restore — every
     namespace will need to be re-added with its plaintext connection string.

3. **Back up the current (about-to-be-replaced) state**, even if it's broken — you may need to
   compare against it, and this restore procedure is otherwise a one-way door:
   ```bash
   mv /var/servicehub/data/servicehub-dlq.db /var/servicehub/data/servicehub-dlq.db.pre-restore
   mv /var/servicehub/data/servicehub-dlq.db-wal /var/servicehub/data/servicehub-dlq.db-wal.pre-restore 2>/dev/null
   mv /var/servicehub/data/servicehub-dlq.db-shm /var/servicehub/data/servicehub-dlq.db-shm.pre-restore 2>/dev/null
   mv /var/servicehub/data/servicehub-namespaces.json /var/servicehub/data/servicehub-namespaces.json.pre-restore 2>/dev/null
   ```
   The `-wal`/`-shm` files are SQLite's write-ahead-log sidecar files from the running instance;
   they must not be left behind for the restored database to load cleanly. `VACUUM INTO` snapshots
   never produce their own `-wal`/`-shm` files, so the bundle itself never has any to restore.

4. **Copy the bundle's files into place:**
   ```bash
   cp /path/to/backup/20260828-153000Z/servicehub-dlq.db /var/servicehub/data/servicehub-dlq.db
   cp /path/to/backup/20260828-153000Z/servicehub-namespaces.json /var/servicehub/data/servicehub-namespaces.json  # if present
   ```
   (Adjust paths to match your `DlqDatabase:DataDirectory` / `NamespaceRepository:DataDirectory`
   configuration.)

5. **Start ServiceHub and verify:**
   - Check `/health/ready` returns healthy.
   - Spot-check that expected namespaces appear and DLQ history/audit trail data looks right for
     the backup's timestamp.
   - If a namespace's connection string won't decrypt (encryption key fingerprint mismatch from
     step 2), ServiceHub will surface it as a per-namespace decryption failure rather than
     crashing — re-add that namespace with its plaintext connection string.

6. **Once confident, clean up** the `.pre-restore` files from step 3 (or keep them somewhere safe
   until you're fully confident the restore is correct).

## 5. Triggering a backup

**On-demand**, at any time, via the admin API:

```bash
curl -X POST https://your-servicehub-host/api/v1/admin/backup \
  -H "X-API-Key: <an API key with the admin scope>"
```

Returns the manifest for the bundle just created (`200 OK`).

**Scheduled**, via `Backup:ScheduledBackupIntervalHours` in configuration (or the
`Backup__ScheduledBackupIntervalHours` environment variable). Off by default (`0`) — an operator
opts in explicitly by setting a positive number of hours. An on-demand backup remains available
regardless of this setting.

```json
"Backup": {
  "BackupDirectory": null,
  "ScheduledBackupIntervalHours": 6,
  "RetentionCount": 14
}
```

- **`BackupDirectory`** — where bundles are written. Defaults to a `backups` subfolder under
  `DlqDatabase:DataDirectory`. Point it at separate (larger, or more durable) storage if desired.
- **`RetentionCount`** — how many of the most recent bundles to keep. Older bundles are deleted
  immediately after each successful backup.

List existing bundles (for DR verification / operator visibility) via:

```bash
curl https://your-servicehub-host/api/v1/admin/backup \
  -H "X-API-Key: <an API key with the admin scope>"
```

## 6. What this does *not* do

Deliberately out of scope for this feature:

- No external backup services, S3/blob storage integration, or off-host shipping — bundles land on
  local/mounted disk only; moving them offsite is an operator responsibility (e.g. your own volume
  snapshot or sync job pointed at the backup directory).
- No WAL shipping or continuous/point-in-time recovery — backups are periodic snapshots, not a
  continuous replication stream.
- No automated restore — restore is always the manual procedure in §4.
- No schema changes, database-engine migration, or namespace-store migration — this feature backs
  up what exists today (SQLite + namespace JSON) as-is.
