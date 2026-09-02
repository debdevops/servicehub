# Encryption Key Rotation

ServiceHub encrypts every stored connection string (Azure Service Bus SAS, AWS access keys, GCP
service-account JSON) with AES-GCM under a key derived from `Security:EncryptionKey`. Until this
document, that key could never be changed after the first namespace was added — rotating it made
every stored credential permanently undecryptable, and the product's own error message admitted it:
*"The encryption key may have changed — please re-add this namespace."*

This document describes the multi-key registry that fixes that, and the operator procedures for
normal rotation and for responding to a suspected key compromise. It corresponds to Phases 1–2 of
the `adr-encryption-key-rotation` design (project memory) and to item **W0.3** of
`docs-private/SERVICEHUB-AUTONOMY-AUDIT-AND-ROADMAP-2026-09-01.md`.

## 1. The two envelope formats

| Format | Used when | AAD-authenticated |
|---|---|---|
| `ENC[v1]:{base64}` | `Security:EncryptionKeyRegistry` is **not** set — today's default, single-key deployments. Unchanged from before this document. | No (predates key IDs) |
| `ENC[v2:kid=<id>]:{base64}` | `Security:EncryptionKeyRegistry` **is** set. | Yes — the key ID is bound into the ciphertext as AES-GCM additional authenticated data. Tampering with the envelope (swapping the `kid`) is detected at decrypt time. |

Both formats always remain decryptable regardless of which one is currently being produced —
decryption looks up whichever key ID the envelope names, not just the active one. A namespace is
never silently made unreadable by this change; you always control when (and if) you opt in.

**A single-key deployment does not need to do anything.** `ENC[v1]:` connection strings keep
working exactly as they always have. Configure `Security:EncryptionKeyRegistry` only when you want
the ability to rotate the key going forward.

## 2. Configuration

Set the `SECURITY__ENCRYPTIONKEYREGISTRY` environment variable to a JSON object (never write real
key material into a committed config file):

```bash
export SECURITY__ENCRYPTIONKEYREGISTRY='{
  "ActiveKeyId": "prod-2026-09",
  "Keys": [
    { "Id": "legacy-v1",    "Material": "<your existing 64-hex Security:EncryptionKey value>", "Status": "active" },
    { "Id": "prod-2026-09", "Material": "<new 64-hex key from openssl rand -hex 32>",           "Status": "active" }
  ]
}'
```

- **`ActiveKeyId`** — which key new encryptions use. Must match one of `Keys[].Id`.
- **`Keys[].Id`** — opaque, 1–64 alphanumeric/hyphen characters. Must be unique.
- **`Keys[].Material`** — 64 hex chars (`openssl rand -hex 32`) or a password string (PBKDF2-derived
  either way). Never persisted anywhere by ServiceHub; supply it only via this environment variable
  or an external secret provider.
- **`Keys[].Status`** — `active`, `retired`, or `compromised`. Informational today (only
  `ActiveKeyId` decides which key encrypts new data); `compromised` exists for the future bulk
  re-encryption workflow (Phase 4, not yet built — see §5).
- **`legacy-v1`** is a reserved ID: every connection string encrypted before you configure a
  registry is assumed to use it. **The first time you turn on the registry, you must include your
  prior `Security:EncryptionKey` value under this exact ID**, or every existing namespace becomes
  undecryptable the moment you switch over.

Validation runs at startup and fails fast (not on first request) if: `ActiveKeyId` doesn't match any
key, two keys share an ID, a key ID has an invalid format, a key has empty material, or the JSON
itself is malformed. In `ASPNETCORE_ENVIRONMENT=Production`, `ProductionConfigurationValidator` runs
the same checks before the app is considered ready.

## 3. Normal rotation procedure

1. Generate new key material:
   ```bash
   openssl rand -hex 32
   ```
2. Update the registry, keeping every previously-active key present (with `Status: "retired"` if you
   like) and pointing `ActiveKeyId` at the new one:
   ```json
   {
     "ActiveKeyId": "prod-2026-10",
     "Keys": [
       { "Id": "legacy-v1",    "Material": "...", "Status": "retired" },
       { "Id": "prod-2026-09", "Material": "...", "Status": "retired" },
       { "Id": "prod-2026-10", "Material": "<new key>", "Status": "active" }
     ]
   }
   ```
3. Restart the ServiceHub process/container. Startup validates the registry and logs a summary:
   `Encryption key registry loaded: 3 key(s) (active=prod-2026-10, mode=multi-key)`.
4. Every existing namespace remains readable — its stored `ENC[v2:kid=...]` (or `ENC[v1]:`) envelope
   still names whichever key encrypted it, and that key is still in the registry.
5. New namespaces created from this point on are encrypted under `prod-2026-10`.

**Existing namespaces are not proactively re-encrypted under the new key** (Phase 1–2 ships lazy
migration only: a namespace is re-encrypted to the active key the next time its plaintext connection
string is supplied to `Protect` again — today, in practice, that only happens if you delete and
re-add it, since there is no in-place connection-string-update endpoint yet). This is a deliberate,
narrow scope: it means "you cannot rotate the key" is fixed — nothing is ever lost on rotation — and
proactive bulk migration off a merely-rotated (not compromised) key is a lower-severity backlog item,
not this fix's job.

## 4. Verifying which key protects a namespace

Every backup manifest (see `docs/BACKUP-RESTORE.md` §2) records `encryptionKeyFingerprint` — a
non-reversible `sha256:<16 hex>` fingerprint of whichever key was *active* when the backup was
taken, never the key material itself. Compare fingerprints across environments (e.g. before
restoring a backup) to confirm they were produced under compatible key configurations. There is no
live endpoint exposing the current fingerprint outside of a backup run today.

## 5. Compromise response — what exists today, and what doesn't

If a key is known or suspected to be compromised:

1. Rotate immediately per §3, and mark the compromised key `"Status": "compromised"` in the registry
   instead of `"retired"` — this is recorded for operator visibility and for the re-encryption
   service described next, but does **not** by itself revoke the key: it is still in the registry
   and can still decrypt data, which is intentional — see step 3.
2. **Not yet built:** an automated bulk re-encryption worker that scans every namespace encrypted
   under the compromised key and re-encrypts it to the active key, so the compromised key can be
   fully retired. This is Phase 4 of the ADR (`EncryptionKeyReEncryptionService`,
   `GET /api/v1/admin/re-encryption-status`) and is intentionally deferred — build it when an actual
   compromise makes it needed, not speculatively.
3. **Until Phase 4 exists**, the operator-driven remediation path is: for each namespace you believe
   was encrypted under the compromised key, delete it and re-add it with its (still-valid, unless the
   underlying cloud credential was also rotated) connection string. This re-encrypts it under the
   active key on write. Do not remove the compromised key from the registry until you have confirmed
   every namespace that depended on it has been re-added — removing it first makes those namespaces'
   stored connection strings permanently undecryptable, which is the exact failure mode this document
   exists to prevent.

## 6. What this does not change

- The eligibility gate, the Recovery Evidence Ledger, and every other safety invariant are
  untouched — this is a storage-layer change to how one field is encrypted.
- No new database table or column exists for this. The registry lives entirely in configuration
  (an environment variable or external secret provider), never in the SQLite database or any
  committed file.
- AWS access keys and GCP service-account JSON are protected by the exact same envelope as Azure
  SAS connection strings — `IConnectionStringProtector` is provider-agnostic.
