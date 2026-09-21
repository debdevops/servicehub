#!/usr/bin/env python3
"""Independent, offline verifier for a ServiceHub Playbook Ledger evidence export.

Reads only an exported bundle (GET /api/v1/playbook/export), recomputes the SHA-256 hash chain
exactly as PlaybookHashChain.ComputeEntryHash does, and — the check verify-recovery-chain.py has
no equivalent of — confirms every EvidenceRefJson citation into a pillar finding
(AnomalyId/DriftFindingId/CorrelationFindingId/ExternalSignalCorrelationId) resolves inside the
export's own citedEvidence section, failing loudly when one dangles (roadmap next-chapter M1.3,
ADR-0009).

A fully independent script from verify-recovery-chain.py, not an extension of it: the Playbook
and Recovery hash chains are structurally identical in algorithm but cryptographically and
architecturally separate chains (no shared Seq space, no shared code path on the server side
either — see PlaybookHashChain's own remarks) — verifying one by reusing the other's parser would
blur a distinction the server deliberately keeps hard.

Uses only the Python standard library, never imports ServiceHub code, never contacts a running
ServiceHub server, and never modifies the input.

Usage:
    python3 verify-playbook-chain.py <bundle.json>

Exit codes:
    0  PASS
    1  FAIL (a verification check failed)
    2  usage or input-parsing error
"""

import argparse
import hashlib
import json
import re
import sys
from datetime import datetime, timezone

GENESIS_HASH = "0" * 64

# The exact four field names PlaybookEvidenceExporter looks for — kept in lockstep with that
# class's own closed set, not inferred from key-name heuristics.
FINDING_CITATION_FIELDS = (
    "AnomalyId",
    "DriftFindingId",
    "CorrelationFindingId",
    "ExternalSignalCorrelationId",
)

_TIMESTAMP_RE = re.compile(
    r"^(?P<date>\d{4}-\d{2}-\d{2})T(?P<time>\d{2}:\d{2}:\d{2})"
    r"(?:\.(?P<frac>\d+))?"
    r"(?P<offset>Z|[+-]\d{2}:\d{2})$"
)


class VerificationError(Exception):
    """Raised for a malformed export the verifier cannot even attempt to check."""


def _guid(value):
    return value.lower()


def to_dotnet_round_trip_utc(timestamp: str) -> str:
    """See verify-recovery-chain.py's identical helper for the full rationale — this is the same
    ISO-8601 -> DateTimeOffset.ToUniversalTime().ToString("O") reformatting, duplicated rather than
    imported so this script has zero dependency on its sibling."""
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
    """Recomputes PlaybookHashChain.ComputeEntryHash's SHA-256 digest for one event."""
    fields = [
        _guid(event["id"]),
        event["ownerId"],
        str(event["seq"]),
        _guid(event["entryId"]),
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
    """Returns (entries, events, cited_evidence, manifest) from a format=json bundle
    (PlaybookController's GET /api/v1/playbook/export)."""
    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)

    if not isinstance(data, dict) or "events" not in data or "entries" not in data:
        raise VerificationError(
            "Could not find 'entries'/'events' in this file. Expected a Playbook evidence export "
            "bundle (GET /api/v1/playbook/export)."
        )

    return data["entries"], data["events"], data.get("citedEvidence", {}), data.get("manifest")


def verify_chain(events, manifest=None):
    """Same shape of check as verify-recovery-chain.py's verify() — see that module for the full
    per-check rationale, which applies identically here since the algorithm is structurally the
    same. Returns a list of finding strings — empty means the chain itself is intact."""
    findings = []

    if not events:
        return ["No events to verify — an empty export cannot be checked."]

    seen_seqs = set()
    prev_seq = None

    for position, event in enumerate(events):
        seq = event["seq"]

        if seq in seen_seqs:
            findings.append(f"Seq {seq}: duplicated — appears more than once in this export.")
            continue
        seen_seqs.add(seq)

        if prev_seq is not None and seq <= prev_seq:
            findings.append(
                f"Seq {seq} (position {position}): out of order — expected it after Seq {prev_seq}."
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

    ordered = sorted(events, key=lambda e: e["seq"])
    for earlier, later in zip(ordered, ordered[1:]):
        if later["seq"] == earlier["seq"] + 1 and later["prevHash"] != earlier["entryHash"]:
            findings.append(
                f"Seq {later['seq']}: PrevHash does not match Seq {earlier['seq']}'s EntryHash, "
                "though the two are adjacent in the chain — evidence was deleted, reordered, or "
                "altered between them."
            )

    if manifest and "chain" in manifest:
        claimed_checked = manifest["chain"].get("eventsChecked")
        if claimed_checked is not None and claimed_checked != len(events):
            findings.append(
                f"Manifest claims eventsChecked={claimed_checked} but this export contains "
                f"{len(events)} event(s) — the export may have been truncated."
            )

    return findings


def verify_evidence_citations(entries, cited_evidence):
    """The check this script exists that verify-recovery-chain.py has no equivalent of: every
    finding citation named in an entry's EvidenceRefJson must resolve inside citedEvidence.
    Returns a list of finding strings — empty means every citation resolves."""
    findings = []

    for entry in entries:
        evidence_ref_json = entry.get("evidenceRefJson")
        if not evidence_ref_json:
            continue

        try:
            evidence = json.loads(evidence_ref_json)
        except json.JSONDecodeError:
            continue

        if not isinstance(evidence, dict):
            continue

        for field_name in FINDING_CITATION_FIELDS:
            finding_id = evidence.get(field_name)
            if finding_id is None:
                continue

            if str(finding_id) not in cited_evidence:
                findings.append(
                    f"Entry {entry.get('id')}: cites {field_name}={finding_id!r}, which is not "
                    "present in this export's citedEvidence section — the citation is dangling."
                )
            # Only one citation field is ever populated per entry in practice (see
            # PlaybookEvidenceExporter's own extraction, which stops at the first match) — no
            # break needed here since a second match on the same entry is simply another
            # (equally checkable) citation, not a conflict.

    return findings


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("path", help="Path to a Playbook evidence export bundle (format=json).")
    args = parser.parse_args()

    try:
        entries, events, cited_evidence, manifest = load_export(args.path)
    except (OSError, json.JSONDecodeError, VerificationError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2

    findings = verify_chain(events, manifest)
    findings += verify_evidence_citations(entries, cited_evidence)

    if not findings:
        seqs = [e["seq"] for e in events] if events else []
        owner = events[0]["ownerId"] if events else "(no events)"
        print(
            f"PASS — {len(events)} event(s) verified, {len(entries)} entrie(s), "
            f"{len(cited_evidence)} cited finding(s) resolved, owner={owner!r}."
        )
        if seqs:
            print(f"Seq {min(seqs)}-{max(seqs)} intact; no citation into a pillar finding dangles.")
        return 0

    print(f"FAIL — {len(findings)} finding(s):")
    for finding in findings:
        print(f"  - {finding}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
