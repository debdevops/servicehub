# Recovery Evidence Ledger

> **In this article:** how ServiceHub records every recovery decision, how to export that record, and how an auditor can verify it **offline, without ServiceHub** —
> including exactly what the record can and cannot prove.
>
> **In plain language:** every time ServiceHub replays or deletes a stuck ("dead-lettered") message — or refuses to — it writes down what it did, who or what decided,
> and what happened afterward, in a way that cannot be quietly edited later. Think of it as a flight recorder for your queues. You read it in the app on **Advanced → Recovery
> Ledger**; this page is for someone who needs to verify it independently (an auditor, a compliance reviewer, an engineer chasing a discrepancy).

The ledger is a durable, append-only, hash-chained record kept in ServiceHub's SQLite database. It is honest about a hard limit up front: the chain is
**tamper-evident, not tamper-proof.** Anyone with write access to the database file can recompute the whole chain and produce a self-consistent forgery. Verification
detects a changed field, a wrong link or a gap — not a determined adversary with database access. There is no cryptographic signing or external notarisation.

## 1. Data model

- **`RecoveryOperation`** — the immutable header of one decision: who, why, what scope (one message, a rule firing, a bulk job).
- **`RecoveryLedgerEntry`** — one per (operation, message): that message's own lifecycle, plus a snapshot of namespace, provider, entity and body-hash at the moment recovery
  began. A small, declared set of fields (state, verification result/confidence, observation window, marker, closed-at) may change as the entry progresses; everything
  else is immutable, and each change is itself recorded as an event.
- **`RecoveryEvent`** — the evidence. Append-only and hash-chained; never updated, never deleted.

The append-only rule is enforced where data is saved (`RecoveryLedgerAppendOnlyGuard`): a save that would modify or delete an operation or event — or change an entry field outside
the declared set — throws. No controller, executor or agent can bypass it.

## 2. Entry lifecycle

```
Executing → Observing → Recovered        did not come back, with full scan coverage of the watch window
                      → Returned         came back within the window
                      → Unverified       window closed without adequate coverage — NOT a failure: the replay may have worked
          → ExecutionFailed              the cloud rejected the call
          → ExecutionUnknown             the process stopped mid-call, or lost contact before the answer was recorded — outcome genuinely unknown
Executing → Discarded                    a purge was accepted (deliberate destruction)
(before any cloud call) → Declined       the eligibility gate stopped it; nothing was sent
(any open state) → WrittenOff / Expired  an operator declared it unrecoverable (a reason is required) / it aged out
```

**An attempt with no recorded answer.** A replay writes its entry as `Executing` *before* it calls the cloud, then records the answer. If ServiceHub stops in between, the
cloud may or may not have put the message back, and the dead letter can still look active. So:

- At startup, every `Executing` entry that began **before this process started** becomes `ExecutionUnknown`. One instance runs at a time (the instance lock), so such an entry cannot belong to a
  live call. It is neither a success nor a failure, it stays open, and a restart never changes it again.
- While any attempt on a message is `Executing` or `ExecutionUnknown`, **no other attempt on that message may begin** — manual, approved, bulk or automatic. The eligibility gate refuses it
  (`REPLAY_OUTCOME_UNKNOWN`) and the ledger refuses to open a second entry, so two attempts racing past the gate cannot both start.
- It appears as pending work ("An earlier attempt has no recorded answer"). A person with the Approver role looks at the queue and records what they found; the entry then closes as
  **written off** in their words — never `Recovered`, never `Failed`. Only after that can a fresh, checked attempt be made.
- ServiceHub does not retry an ambiguous send, and it cannot tell from the cloud whether it happened: the three clouds add the new copy before they remove the original, so the
  window in which both exist is real.

`Recovered` means *"a replayed message did not reappear in the dead-letter queue for the whole observation window, and ServiceHub had continuous, uncapped scan coverage of
it."* It never means the downstream business transaction succeeded — ServiceHub cannot see past the queue.

## 3. The hash chain

The chain is partitioned **per owner** (`OwnerId`); `Seq` is 1, 2, 3 … with no gaps. Verifying one event means verifying the owner's chain up to it.

- **Genesis:** the first event has `PrevHash` = 64 ASCII `0` characters.
- **`EntryHash`** is the lowercase hex SHA-256 of the UTF-8 bytes of these twelve fields joined with `|`, in this order:

```
1 id (GUID "D" format)              5 operationId (GUID "D")               9  actorKind (enum NAME)
2 ownerId                           6 eventType (enum NAME)                10 detailJson (empty string if null)
3 seq (invariant integer)           7 occurredAt (UTC, "O" round-trip)     11 schemaVersion (integer)
4 entryId (GUID "D", empty if null) 8 actorIdentity                        12 prevHash
```

**It is a pipe-joined string, not a JSON serialisation.** Hashing the raw JSON will not reproduce it. Reproduction notes: enums are their **names** (`EntryBegun`, not `1`), which
is how the export writes them; `occurredAt` must be re-formatted as UTC with 7 fractional digits and a `+00:00` offset — not a trailing `Z` — because a byte-for-byte match
matters; a null `entryId` or `detailJson` is the empty string, never the text `null`.

**Verifying a chain,** for one owner's events in `Seq` order: `Seq` must equal the expected next number (a gap means a missing or reordered event); `PrevHash` must equal the
previous event's `EntryHash`; recomputing `EntryHash` must match the stored one (a mismatch means the event's own fields were altered). Report the first `Seq` that fails, and which check.

`GET /api/v1/recovery/chain` runs exactly this on the server: `{"ownerId":"…","isValid":true,"eventsChecked":15424,"firstDivergentSeq":null,"reason":null}`.
That is the server marking its own homework — hence the offline verifier below.

## 4. Export and independent offline verification

**Export.** Advanced → Recovery Ledger → *Export evidence* (choose a window), or `GET /api/v1/recovery/export[?from=…&to=…]` (not from a key limited to some
namespaces — the ledger is one chain across all of them). One JSON file, `servicehub-evidence-<from>-to-<to>.json`:

```json
{ "manifest": { "kind": "servicehub-recovery-evidence", "exportedAt": "…", "from": null, "to": null, "partial": false,
                "note": "The whole chain, from its first event.",
                "chain": { "firstSeq": 1, "lastSeq": 15424, "eventsInChain": 15424 },
                "verify": "python3 verify-recovery-chain.py servicehub-evidence-….json" },
  "events":   [ { "id": "…", "ownerId": "…", "seq": 1, "entryId": null, "operationId": "…", "eventType": "OperationOpened",
                  "occurredAt": "…", "actorIdentity": "…", "actorKind": "User", "detailJson": null, "schemaVersion": 1,
                  "prevHash": "000…0", "entryHash": "…" }, … ] }
```

`partial: true` means a `from`/`to` window was applied, so the file is a slice of the chain, not all of it.

**Verify it** with `scripts/verify-recovery-chain.py` — Python 3 standard library only; it never contacts a server or database and never modifies its input:

```
$ python3 scripts/verify-recovery-chain.py servicehub-evidence-20260925-0603-to-20260929-1653.json
PASS — 15424 event(s) verified, owner='__spa__', Seq 1-15424.
```

It recomputes every `EntryHash` from the twelve fields and checks that `Seq` strictly increases, that adjacent events chain (`PrevHash` = the previous `EntryHash`), that only `Seq` 1 carries the
genesis `PrevHash`, and that the manifest's range matches the events. Exit `0` = PASS, `1` = FAIL (naming every divergent `Seq`), `2` = the input could not be parsed.

A tampered copy — one field of one event changed — fails and says where (a real run, 2026-09-29, `eventType` of one event edited):

```
FAIL — 1 finding(s):
  - Seq 3001: EntryHash mismatch — stored=df6922ac… recomputed=7cf051d4… This event's fields were altered after being appended.
```

**What PASS proves:** nothing in the file was altered, reordered, duplicated or dropped relative to itself and its manifest. **What it cannot prove:** that the file is the *whole* ledger
(`partial: true` is a slice, and a slice cannot show what sat outside it), or that the database was not rewritten wholesale before export — the tamper-evident-not-tamper-proof limit above. The script also
carries an `--archive-dir` option for 4.0.0's sealed-epoch archives; ServiceHub 4.1.0 and later do not seal epochs, so leave it unused. (A message it prints about a "per-operation export" is 4.0.0 wording; an export is the whole chain unless `partial` is true.)

## 5. What ServiceHub can and cannot prove

Recovery verification depends on ServiceHub being able to *observe the dead-letter queue* after a replay, and that differs by cloud:

| Provider | Can prove absence (`Recovered` reachable) | Why |
|---|---|---|
| Azure Service Bus | Yes | A non-destructive peek gives continuous, uncapped visibility of the DLQ. |
| AWS SQS | **No** | There is no non-destructive peek; scanning the DLQ would disturb receive counts. Its entries close `Unverified`. |
| GCP Pub/Sub | **No** | Scanning is capped per cycle. Its entries close `Unverified`. |

`CanProveDlqAbsence = false` **structurally** stops that provider's entries reaching `Recovered` — it is enforced where the outcome is decided, not a UI label. Regardless of cloud, the ledger never
establishes whether any consumer processed the message, whether the business transaction completed, or anything about a message removed by another system.

## 6. The recovery marker

Where the cloud allows it (`ProviderCapabilities.SupportsRecoveryMarker`), a replayed message carries an application property `x-servicehub-recovery-id` set to the entry's id, so a later reappearance in the
DLQ is attributed to that exact recovery (confidence **Exact**). Where it cannot be applied, recurrence is matched by body hash (**Heuristic**), and if more than one open entry shares the hash the match is recorded as
ambiguous rather than guessed.

*Verified live 2026-09-29 against a real 15,424-event ledger: chain valid, offline verifier PASS, tamper negative control FAIL at the edited `Seq`.*
