# Recovery Evidence Ledger

ServiceHub's Recovery Evidence Ledger is a durable, append-only, hash-chained record of every
recovery decision ServiceHub makes on a dead-lettered message — replay or purge — and, where
provable, its eventual outcome. This document is the standalone reference for an auditor working
from an exported evidence bundle alone, with no access to the ServiceHub source or database.

It is honest about a hard limit up front: the chain is **tamper-evident, not tamper-proof**.
Anyone with write access to the underlying SQLite file can recompute the entire chain and produce
a self-consistent forgery. Verification here detects *casual or partial* alteration — a changed
field, a wrong link, a gap in sequence — not a determined adversary with database access. There is
no cryptographic signing or external notarization in this release (deliberately deferred; see the
project roadmap).

## 1. Data model

Three entity types, in a strict hierarchy:

- **`RecoveryOperation`** — the immutable header for one decision: who, why, what scope (a single
  message, a rule firing, a bulk job), and when it was opened. Never mutated after insert.
- **`RecoveryLedgerEntry`** — one per (operation, message). Tracks the message's own lifecycle —
  `Executing → Observing → Recovered/Returned/Unverified/...` — snapshotting namespace, provider,
  entity, and body-hash identity at the moment recovery began. A small, explicitly-enumerated set
  of fields (state, verification result/confidence, observation window, marker, closed-at) may be
  updated as the entry progresses; every other field is immutable once inserted, and every such
  update is itself recorded as an event (§3).
- **`RecoveryEvent`** — the evidence itself. Append-only, hash-chained, and the only place a fact
  is ever recorded twice: once as a durable row, once folded into the chain. Never updated, never
  deleted.

An operation has one or more entries; an entry accumulates one or more events over its lifetime.

## 2. Entry lifecycle

```
Executing → Observing → Recovered      (no recurrence, full coverage — "did not return")
                       → Returned       (recurrence observed within the window)
                       → Unverified     (window closed without adequate coverage)
          → ExecutionFailed             (provider rejected the call)
          → ExecutionUnknown            (process died mid-call — outcome genuinely unknown)
Executing → Discarded                   (purge accepted — deliberate destruction)
(any non-terminal) → WrittenOff         (operator declared unrecoverable; requires a reason)
(any non-terminal) → Expired            (aged past threshold; reachable only after an
                                          AgeingFlagged event was recorded for the entry first)
```

`Recovered` means *"a replayed message did not reappear in the dead-letter queue for the full
observation window, and ServiceHub had continuous, uncapped scan coverage of that window."* It
never means the downstream business transaction succeeded — ServiceHub cannot see past the queue.

## 3. The hash chain

The chain is partitioned **per owner** (`OwnerId`), not per operation and not globally. `Seq` is a
monotonically increasing integer, unique per `(OwnerId, Seq)`, starting at 1. Verifying any one
operation's evidence necessarily verifies its owner's entire chain up to the present, because the
events interleave across all of that owner's operations in one sequence.

### 3.1 Genesis

The first event for a given owner has `PrevHash` equal to 64 ASCII `'0'` characters (the same
length as a SHA-256 hex digest).

### 3.2 EntryHash computation

For every `RecoveryEvent`, `EntryHash` is the lowercase hex-encoded SHA-256 digest of the UTF-8
bytes of the following fields, joined with the ASCII pipe character `|` in exactly this order:

```
1.  Id              (GUID, "D" format — lowercase, hyphenated, no braces)
2.  OwnerId          (raw string)
3.  Seq              (integer, invariant culture)
4.  EntryId           (GUID "D" format, or empty string if null — operation-level events only)
5.  OperationId       (GUID, "D" format)
6.  EventType         (enum name — e.g. "EntryBegun", "RecurrenceObserved" — not its numeric value)
7.  OccurredAt        (UTC, ISO-8601 round-trip format: DateTimeOffset.ToUniversalTime().ToString("O"))
8.  ActorIdentity     (raw string)
9.  ActorKind         (enum name — e.g. "Human", "Automation", "System")
10. DetailJson        (raw string, or empty string if null)
11. SchemaVersion     (integer, invariant culture)
12. PrevHash          (the previous event's EntryHash for this owner; 64 zeros for the first event)
```

That is: `EntryHash = lowercase_hex(SHA256(field1 + "|" + field2 + "|" + ... + "|" + field12))`.

**This is a pipe-delimited canonical string, not a JSON serialization.** Concatenating the raw
JSON representation of an exported event will not reproduce the hash — you must extract the twelve
fields above and join them in this exact order with `|` before hashing.

Notes for reproduction from an exported bundle:

- `EventType` and `ActorKind` must be rendered as their **enum name**, not the small integer
  ServiceHub uses internally (`EntryBegun`, not `1`). The export JSON already serializes them as
  strings.
- `OccurredAt`: the export JSON's `occurredAt` field is a standard ISO-8601 timestamp. Parse it,
  convert to UTC if it isn't already, and re-format with .NET's round-trip (`"O"`) specifier — or
  equivalently, an ISO-8601 string with 7 fractional-second digits and a `+00:00` UTC offset (not
  a trailing `Z`). Byte-for-byte agreement with the original matters; a naive re-stringification
  in another language's default ISO-8601 formatter will usually **not** match.
- `EntryId` is empty string, not the literal text `"null"`, when the event is operation-level.
- `DetailJson` is empty string, not `"null"`, when absent.

### 3.3 Verifying a chain

Given a `Seq`-ordered list of one owner's events:

1. Set `expectedPrevHash = genesis (64 zeros)`, `expectedSeq = 1`.
2. For each event in order:
   a. Its `Seq` must equal `expectedSeq` — any gap means a missing or reordered event.
   b. Its `PrevHash` must equal `expectedPrevHash` — a mismatch means the chain was broken or
      reordered at this point.
   c. Recompute `EntryHash` per §3.2 and compare to the stored value — a mismatch means this
      event's own fields were altered after being appended.
   d. On any failure, stop and report this `Seq` as the first divergent point, with which of (a),
      (b), or (c) failed.
   e. On success, set `expectedPrevHash = <this event's EntryHash>`, `expectedSeq += 1`.
3. If every event passes, the chain is intact.

This is exactly what `RecoveryChainVerifier.Verify` (server-side) and the evidence export's
`chain.verified` field report — an independent recomputation should agree with it.

### 3.3a Independent offline verification

`scripts/verify-recovery-chain.py` is a dependency-free (Python 3 standard library only) tool
that recomputes the checks above **without running ServiceHub and without trusting its API to
say "valid."** It never contacts a server, never touches a database, and never modifies its
input.

**1. Obtain the evidence package.** `GET /api/v1/recovery/operations/{id}/export?format=package`
(or `format=json` for the combined bundle — see §6) — either works as input to the script.

**2. Run the verifier:**

```
python3 scripts/verify-recovery-chain.py recovery-evidence-<id>-<timestamp>.zip
```

**3. What it verifies**, for every event in the export:
- Its `EntryHash` recomputes correctly from its own twelve canonical fields plus its own stored
  `PrevHash` (§3.2) — detects an event modified after being appended.
- `Seq` values are strictly increasing across the export, with no duplicates or reordering.
- Wherever two exported events are truly adjacent in `Seq` (`n`, `n+1`), the second's `PrevHash`
  equals the first's `EntryHash` — detects deletion or reordering of evidence between them.
- Any event whose `PrevHash` is the genesis hash (§3.1) has `Seq == 1` — genesis is only valid
  for the very first event in the owner's entire chain.
- If a `manifest.json`/`manifest` is present, the exported events' `Seq` range matches what the
  manifest claims — detects an event silently dropped from the export after the manifest was
  computed (truncation).

**4. What "PASS" means:** every check above held for every event in this export. Nothing in the
export was altered, reordered, duplicated, or dropped relative to what the manifest (if present)
claims.

**5. What it cannot prove:** continuity with the owner's **global** chain. A per-operation export
contains only that operation's events; other operations' events are interleaved between them in
the real, owner-wide sequence (see §3), so gaps in `Seq` between two exported events are normal,
not evidence of tampering — and this tool has no way to see what, if anything, sits in those
gaps. Proving the entire owner chain's continuity requires the full chain (every operation's
events), which only the running ServiceHub server has, or a full owner-wide `events` export. This
is a structural limit of exporting per operation, not a weakness specific to this tool — it is
the same "tamper-evident, not tamper-proof" honesty this document opens with, extended to what an
offline reader can and cannot check.

**6. Example: a valid export**

```
$ python3 scripts/verify-recovery-chain.py recovery-evidence-<id>-<timestamp>.zip
PASS — 3 event(s) verified, owner='acme-owner', Seq 1-3.
This confirms: no event was altered after being appended, no event in this export
is missing/duplicated/reordered, and adjacent-Seq events chain correctly.
This does NOT confirm continuity with other operations' events in the owner's
global chain — see docs/RECOVERY-EVIDENCE.md for what an offline, per-operation
export cannot prove.
```

**7. Example: a tampered export** (one field of one event edited after export)

```
$ python3 scripts/verify-recovery-chain.py recovery-evidence-<id>-<timestamp>-tampered.zip
FAIL — 1 finding(s):
  - Seq 2: EntryHash mismatch — stored=21132ab... recomputed=791606b... This event's fields
    were altered after being appended.
```

The script exits `0` on PASS, `1` on FAIL (naming every divergent `Seq`), and `2` if the input
couldn't be parsed at all.

## 4. What ServiceHub can and cannot prove

Recovery verification depends on ServiceHub actually being able to observe the dead-letter queue
after a replay. That capability differs by provider:

| Provider | Can prove absence (`Recovered` is reachable) | Why |
|---|---|---|
| Azure Service Bus | Yes | Non-destructive peek gives continuous, uncapped DLQ visibility. |
| AWS SQS | **No** | No non-destructive peek exists; scanning the DLQ risks altering receive counts. Entries close as `Unverified` with limitation code `AWS_NO_ABSENCE_PROOF`. |
| GCP Pub/Sub | **No**, beyond a capped scan | Scanning is capped per cycle; once the cap is hit, reconciliation for the remainder is skipped. Entries close as `Unverified` with limitation code `GCP_NO_ABSENCE_PROOF`. |

`CanProveDlqAbsence = false` for a provider **structurally** blocks that provider's entries from
ever reaching `Recovered` — it is not a UI label choice, it is enforced where the verification
outcome is decided. An export's manifest always lists which limitations applied and to how many
entries; it is never silently omitted.

Regardless of provider, ServiceHub's evidence never establishes:

- Whether any consumer processed the message successfully.
- Whether the corresponding business transaction completed.
- Anything about a message removed by a system other than ServiceHub.
- For entries where the recovery marker could not be applied (`markerApplied: false`), which
  specific message a body-hash recurrence match refers to, if more than one candidate matched.

Every export's manifest states this explicitly in a `whatServiceHubDoesNotKnow` field — non-empty
on every export, by construction. An export that claimed to know everything would be a worse
product than one that says nothing at all.

## 5. Recovery marker

On providers that support it, a replayed message carries an application-property marker
(`x-servicehub-recovery-id`, set to the ledger entry's ID) so a later recurrence in the DLQ can be
attributed to a specific recovery attempt with certainty, rather than inferred from a body-hash
match. When the marker could not be applied (provider or message-size limits), recurrence detection
falls back to a body-hash heuristic; if more than one open entry shares that hash, the match is
recorded as ambiguous rather than guessed.

## 6. Export bundle contents

An evidence export (`GET` on the recovery operation's export endpoint) supports three formats via
the `format` query parameter:

- `format=json` (default) — the combined bundle: a single document with `manifest`, `operation`,
  `entries`, and `events` under one root. This *is* the "bundle" — there is no separately-named
  `bundle.json` file; requesting the default format is how you get the combined document.
- `format=csv` — `entries.csv` alone, nothing else.
- `format=package` — a zip containing five files, each individually reproducible from the
  `format=json` bundle's fields:
  - `manifest.json` — the honesty contract: schema/service version, chain summary (§3.3's
    `verified` result plus first/last `Seq`), entry counts by state, what ServiceHub knows /
    observed / does not know, and any provider limitations that applied.
  - `operation.json` — the operation header.
  - `entries.json` / `entries.csv` — one row per ledger entry.
  - `events.json` — every event for the operation, `Seq`-ordered, sufficient to run the §3.3
    verification procedure independently.

**Reproducibility**: two exports of the same, unchanged operation are byte-identical except for
`manifest.exportedAt` and `manifest.exportedBy` — entries are ordered deterministically
(`BegunAt`, then `Id`) rather than relying on database-level tie-breaking, so re-exporting is safe
to diff.

## 7. Demo Mode

Evidence generated from Demo Mode fixtures is never presented as real. Every Demo Mode export sets
`manifest.demoMode: true`, adds an explicit `whatServiceHubDoesNotKnow` entry stating the export is
fixture data, carries a `DEMO_DATA_NOT_REAL_EVIDENCE` limitation, and is named with a `demo-` filename
prefix. The watermark travels with the artifact at every layer — UI, manifest, and filename — not
just as a page banner that a downloaded file would lose.

## 8. Append-only enforcement

The three ledger tables are protected at the persistence layer, independent of any caller's
discipline: every `SaveChanges`/`SaveChangesAsync` call is inspected before it commits, and it
throws if it would delete or modify a `RecoveryOperation` or `RecoveryEvent` row, or modify a
`RecoveryLedgerEntry` field outside a small, explicitly-declared mutable set (state, verification
result/confidence, observation window, marker, closed-at). There is no code path — controller,
executor, or worker — that can construct a `RecoveryEvent` update or delete and have it commit.
