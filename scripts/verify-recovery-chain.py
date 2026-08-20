#!/usr/bin/env python3
"""Independent, offline verifier for a ServiceHub Recovery Evidence Ledger export.

Reads only an exported events.json (or a format=package zip / format=json bundle containing
one), and recomputes the SHA-256 hash chain exactly as documented in docs/RECOVERY-EVIDENCE.md
section 3.2. Uses only the Python standard library, never imports ServiceHub code, never
contacts a running ServiceHub server, and never modifies the input.

Usage:
    python3 verify-recovery-chain.py <events.json | bundle.json | export.zip>

Exit codes:
    0  PASS
    1  FAIL (a verification check failed)
    2  usage or input-parsing error

See docs/RECOVERY-EVIDENCE.md section "Independent offline verification" for what this tool
can and cannot prove.
"""

import argparse
import hashlib
import json
import re
import sys
import zipfile
from datetime import datetime, timezone

GENESIS_HASH = "0" * 64

_TIMESTAMP_RE = re.compile(
    r"^(?P<date>\d{4}-\d{2}-\d{2})T(?P<time>\d{2}:\d{2}:\d{2})"
    r"(?:\.(?P<frac>\d+))?"
    r"(?P<offset>Z|[+-]\d{2}:\d{2})$"
)


class VerificationError(Exception):
    """Raised for a malformed export the verifier cannot even attempt to check."""


def _guid(value):
    """Normalises a GUID string to .NET's "D" format (lowercase, hyphenated, no braces) —
    the format System.Text.Json already emits by default, so this is a defensive lower() only."""
    return value.lower()


def to_dotnet_round_trip_utc(timestamp: str) -> str:
    """Reformats an ISO-8601 timestamp to exactly what
    DateTimeOffset.ToUniversalTime().ToString("O") produces: 7 fractional-second digits and a
    "+00:00" UTC offset, never a trailing "Z".

    Fractional digits are preserved verbatim (never round-tripped through a datetime object,
    which only has microsecond/6-digit resolution) so a genuine 100ns-precision timestamp is
    never corrupted by this tool. Shifting to UTC when the source offset isn't already zero only
    ever moves the date/hour/minute/second components — by construction, an ISO-8601 offset is a
    whole number of minutes, so it can never change the seconds-or-finer portion of an instant.
    """
    match = _TIMESTAMP_RE.match(timestamp.strip())
    if not match:
        raise VerificationError(f"Unrecognized timestamp format: {timestamp!r}")

    frac = (match.group("frac") or "").ljust(7, "0")[:7]
    offset = match.group("offset")

    if offset in ("Z", "+00:00", "-00:00"):
        return f"{match.group('date')}T{match.group('time')}.{frac}+00:00"

    dt = datetime.fromisoformat(f"{match.group('date')}T{match.group('time')}.{frac[:6]}{offset}")
    dt_utc = dt.astimezone(timezone.utc)
    return dt_utc.strftime("%Y-%m-%dT%H:%M:%S") + f".{frac}+00:00"


def compute_entry_hash(event: dict) -> str:
    """Recomputes RecoveryHashChain.ComputeEntryHash's SHA-256 digest for one event."""
    entry_id = event.get("entryId")
    fields = [
        _guid(event["id"]),
        event["ownerId"],
        str(event["seq"]),
        _guid(entry_id) if entry_id else "",
        _guid(event["operationId"]),
        event["eventType"],
        to_dotnet_round_trip_utc(event["occurredAt"]),
        event["actorIdentity"],
        event["actorKind"],
        event.get("detailJson") or "",
        str(event["schemaVersion"]),
        event["prevHash"],
    ]
    canonical = "|".join(fields)
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def load_export(path: str):
    """Returns (events, manifest_or_None) from a .zip package, a format=json bundle, or a raw
    events.json array."""
    if path.endswith(".zip"):
        with zipfile.ZipFile(path) as archive:
            names = set(archive.namelist())
            if "events.json" not in names:
                raise VerificationError("Zip package does not contain events.json.")
            events = json.loads(archive.read("events.json"))
            manifest = json.loads(archive.read("manifest.json")) if "manifest.json" in names else None
            return events, manifest

    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)

    if isinstance(data, list):
        return data, None
    if isinstance(data, dict) and "events" in data:
        return data["events"], data.get("manifest")

    raise VerificationError(
        "Could not find an events array in this file. Expected either a raw events.json array, "
        "a format=json bundle with a top-level 'events' key, or a format=package zip."
    )


def verify(events, manifest=None):
    """Returns a list of finding strings — empty means PASS. Mirrors the structure of the
    server-side ChainVerificationResult (first divergent Seq + reason) but is scoped to what a
    single operation's export can prove — see the module docstring."""
    findings = []

    if not events:
        return ["No events to verify — an empty export cannot be checked."]

    seen_seqs = set()
    prev_seq = None
    prev_hash_by_seq = {}

    for position, event in enumerate(events):
        seq = event["seq"]

        if seq in seen_seqs:
            findings.append(f"Seq {seq}: duplicated — appears more than once in this export.")
            continue
        seen_seqs.add(seq)

        if prev_seq is not None and seq <= prev_seq:
            findings.append(
                f"Seq {seq} (position {position}): out of order — expected it after Seq {prev_seq}. "
                "events.json is documented as Seq-ordered; this export was reordered or corrupted."
            )
        prev_seq = seq

        try:
            recomputed = compute_entry_hash(event)
        except (KeyError, VerificationError) as exc:
            findings.append(f"Seq {seq}: malformed event, cannot recompute hash ({exc}).")
            continue

        if recomputed != event["entryHash"]:
            findings.append(
                f"Seq {seq}: EntryHash mismatch — stored={event['entryHash']} recomputed={recomputed}. "
                "This event's fields were altered after being appended."
            )

        if event["prevHash"] == GENESIS_HASH and seq != 1:
            findings.append(
                f"Seq {seq}: PrevHash is the genesis hash but Seq is not 1 — genesis is only "
                "valid for the very first event in the owner's entire chain."
            )

        prev_hash_by_seq[seq] = event["entryHash"]

        op_id = event.get("operationId")
        if manifest and op_id and op_id.lower() != manifest.get("operationId", op_id).lower():
            findings.append(f"Seq {seq}: OperationId {op_id} does not match the manifest's claimed operation.")

    # Adjacent-Seq PrevHash linkage: only checkable where two exported events are truly
    # consecutive in the owner's global chain (Seq n, n+1). A gap between them means other
    # operations' events sit in between in the real chain — this export cannot see those, so no
    # claim is made about them (see "what this cannot prove").
    ordered = sorted(events, key=lambda e: e["seq"])
    for earlier, later in zip(ordered, ordered[1:]):
        if later["seq"] == earlier["seq"] + 1 and later["prevHash"] != earlier["entryHash"]:
            findings.append(
                f"Seq {later['seq']}: PrevHash does not match Seq {earlier['seq']}'s EntryHash, "
                "though the two are adjacent in the chain — evidence was deleted, reordered, or "
                "altered between them."
            )

    if manifest and "chain" in manifest:
        claimed_first = manifest["chain"].get("firstSeq")
        claimed_last = manifest["chain"].get("lastSeq")
        actual_first = min(seen_seqs)
        actual_last = max(seen_seqs)
        if claimed_first is not None and claimed_first != actual_first:
            findings.append(
                f"Manifest claims firstSeq={claimed_first} but the export's lowest Seq present is "
                f"{actual_first} — an event may have been dropped from the front of this export."
            )
        if claimed_last is not None and claimed_last != actual_last:
            findings.append(
                f"Manifest claims lastSeq={claimed_last} but the export's highest Seq present is "
                f"{actual_last} — an event may have been dropped from the end of this export "
                "(truncation)."
            )

    return findings


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("path", help="Path to events.json, a format=json bundle, or a format=package zip.")
    args = parser.parse_args()

    try:
        events, manifest = load_export(args.path)
    except (OSError, json.JSONDecodeError, VerificationError, zipfile.BadZipFile) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2

    findings = verify(events, manifest)

    if not findings:
        seqs = [e["seq"] for e in events]
        owner = events[0]["ownerId"]
        print(f"PASS — {len(events)} event(s) verified, owner={owner!r}, Seq {min(seqs)}-{max(seqs)}.")
        print("This confirms: no event was altered after being appended, no event in this export")
        print("is missing/duplicated/reordered, and adjacent-Seq events chain correctly.")
        print("This does NOT confirm continuity with other operations' events in the owner's")
        print("global chain — see docs/RECOVERY-EVIDENCE.md for what an offline, per-operation")
        print("export cannot prove.")
        return 0

    print(f"FAIL — {len(findings)} finding(s):")
    for finding in findings:
        print(f"  - {finding}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
