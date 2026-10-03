# Encryption Key Rotation

> **In this article:** how ServiceHub protects the cloud credentials you give it, how to change ("rotate") the encryption key without losing access to
> anything, and what to do if you suspect the key has leaked.
>
> **In plain language:** every Azure, AWS or GCP credential you add through *Add a cloud* is effectively a password to your messaging account. ServiceHub
> encrypts it before it is written to disk. This page is about the encryption *key*: how to change it as ordinary hygiene, the way you would rotate any other
> secret, without locking yourself out of clouds you already connected.

Credentials (Azure connection strings, AWS access keys, GCP service-account JSON) are encrypted with AES-256-GCM. The key is **yours**: ServiceHub never
generates or stores it. It comes from `SECURITY__ENCRYPTIONKEY` (one key) or `SECURITY__ENCRYPTIONKEYREGISTRY` (several — this is what makes rotation
possible). A start with neither set **refuses to run** — at startup, not on the first request — with `Neither Security:EncryptionKeyRegistry nor
Security:EncryptionKey is configured`. (`Security:EnableConnectionStringEncryption` defaults to `true`; leave it there.)

> [!IMPORTANT]
> **Changing `SECURITY__ENCRYPTIONKEY` to a new value, on its own, makes every stored credential unreadable.** Never do that. To rotate, move to the
> registry (§2) and keep the old key in it. Losing every key that protects a credential loses that credential: you would re-add the cloud.

## 1. The two envelope formats

Each stored credential carries a prefix that says how it was protected:

| Prefix | When | Bound to its key? |
|---|---|---|
| `ENC[v1]:…` | No registry configured (one key) | No |
| `ENC[v2:kid=<id>]:…` | A registry is configured | Yes — the key id is authenticated data, so swapping the `kid` is detected when it is read |

Reading looks up whichever key the prefix names, not just the active one. A single-key deployment does not have to do anything; configure the registry only when
you want to be able to rotate.

## 2. Configuration

Set the registry as an environment variable holding JSON (never write real key material into a committed file):

```bash
export SECURITY__ENCRYPTIONKEYREGISTRY='{
  "ActiveKeyId": "prod-2026-09",
  "Keys": [
    { "Id": "legacy-v1",    "Material": "<your existing SECURITY__ENCRYPTIONKEY value>", "Status": "retired" },
    { "Id": "prod-2026-09", "Material": "<new key: openssl rand -hex 32>",               "Status": "active"  }
  ]
}'
```

- **`ActiveKeyId`** — the key new credentials are encrypted with. Must be one of `Keys[].Id`.
- **`Keys[].Id`** — 1–64 letters, digits or hyphens; unique.
- **`Keys[].Material`** — 64 hex characters (`openssl rand -hex 32`; derived with HKDF-SHA256) or any password string (derived with PBKDF2, 100,000 iterations).
  ServiceHub never writes it anywhere.
- **`Keys[].Status`** — `active`, `retired` or `compromised` (case-insensitive; anything else fails startup, naming the allowed values). Informational: only
  `ActiveKeyId` decides which key encrypts.
- **`legacy-v1`** is reserved: every credential encrypted before you had a registry is assumed to use it. **The first time you configure a registry, include your
  previous `SECURITY__ENCRYPTIONKEY` value under exactly this id**, or every existing credential becomes unreadable.

An invalid registry (unknown `ActiveKeyId`, duplicate id, bad id format, empty material, malformed JSON) stops ServiceHub at startup, not later.

## 3. Rotate

1. Generate the new key: `openssl rand -hex 32`.
2. Put it in the registry as a new entry, point `ActiveKeyId` at it, and **keep every earlier key** (mark it `retired` if you like).
3. Restart. The log confirms it: `Encryption key registry loaded: 2 key(s) (active=prod-2026-09, mode=multi-key)`.
4. Every existing cloud still works — its prefix names a key that is still in the registry. New clouds are encrypted under the new key.

**Existing credentials move to the new key when they are next saved**, not before. Starting up, listing and opening a cloud change nothing; running **Test connection**
on it re-encrypts it under the active key (there is no edit screen; deleting and re-adding also works). So an old key can be dropped only after every credential that used it has been saved once (§4).

## 4. Which key protects a cloud

There is no screen for this. Read the prefix from the database while ServiceHub is stopped or running (read-only):

```bash
sqlite3 "<data directory>/servicehub.db" "select Name, substr(ConnectionStringEncrypted, 1, 30) from Namespaces"
# orders-dev | ENC[v1]:…                     ← still under the original key (legacy-v1)
# billing    | ENC[v2:kid=prod-2026-09]:…    ← under prod-2026-09
```

To confirm the *active* key is the one you expect, compare fingerprints: **Settings → Access & security** shows the active key's fingerprint (`sha256:` plus 16 hex
characters — one-way, never the key), and every backup's manifest records the same (`docs/BACKUP-RESTORE.md`). Compare them before restoring a backup elsewhere.

**If you drop a key that a credential still needs:** ServiceHub starts and lists the cloud, but using it fails with `Key ID 'legacy-v1' not found in registry. Include the
prior Security:EncryptionKey value under this ID … or re-add this namespace.` Nothing is corrupted. Put the key back in the registry and restart.

## 5. If the key may have leaked

1. Rotate immediately (§3), and mark the leaked key `"Status": "compromised"` rather than `retired`. This records intent; it does **not** revoke the key — it must stay in
   the registry as long as any credential still uses it.
2. Re-protect every cloud: run **Test connection** on each (§3 — this re-encrypts it under the new key), then check with §4 that none still shows the old key.
3. Only then remove the compromised key from the registry and restart.
4. **Rotate the cloud credentials themselves** (new Service Bus policy key, new IAM access key, new service-account key) and delete and re-add each cloud with the new credential — an attacker
   who could read the key and the database could read every credential that was stored under it. Re-encrypting alone does not un-leak a secret.

There is no automated bulk re-encryption job; steps 2–3 are yours to do, cloud by cloud.

## 6. What this does not change

- The eligibility gate, the Recovery Ledger and every other safety invariant are untouched: this is how one stored field is encrypted.
- No table or column exists for this. The registry lives in configuration (an environment variable or your secret provider), never in the database or a committed file.
- AWS and GCP credentials use exactly the same envelope as Azure connection strings.

*Verified 2026-09-29 by running the procedure on a throw-away instance: single key → registry with a new active key → a second cloud came out as `ENC[v2:kid=…]` while the
first stayed `ENC[v1]` until its Test connection → dropping `legacy-v1` while a cloud still used it failed that cloud with the message above and nothing else.*
