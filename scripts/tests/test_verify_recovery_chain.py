"""Tests for scripts/verify-recovery-chain.py.

Run with: python3 -m unittest scripts/tests/test_verify_recovery_chain.py -v

Uses only the standard library (matching the dependency-free verifier itself) — no pytest
dependency for this tool.
"""

import importlib.util
import json
import os
import sys
import tempfile
import unittest
import uuid
import zipfile

_SCRIPT_PATH = os.path.join(os.path.dirname(__file__), "..", "verify-recovery-chain.py")
_spec = importlib.util.spec_from_file_location("verify_recovery_chain", _SCRIPT_PATH)
verifier = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(verifier)

OWNER_ID = "owner-1"
OPERATION_ID = str(uuid.uuid4())


def make_chain(n, start_seq=1):
    """Builds a valid, self-consistent n-event chain starting at start_seq, using the module's
    own compute_entry_hash so these fixtures exercise real hash computation, not a stub."""
    events = []
    prev_hash = verifier.GENESIS_HASH if start_seq == 1 else "f" * 64
    for i in range(n):
        seq = start_seq + i
        entry_id = str(uuid.uuid4()) if i > 0 else None
        event = {
            "id": str(uuid.uuid4()),
            "ownerId": OWNER_ID,
            "seq": seq,
            "entryId": entry_id,
            "operationId": OPERATION_ID,
            "eventType": "EntryBegun" if i > 0 else "OperationOpened",
            "occurredAt": f"2026-08-10T09:{14 + i:02d}:00.1234567+00:00",
            "actorIdentity": "alex@contoso.com",
            "actorKind": "User",
            "detailJson": None,
            "prevHash": prev_hash,
            "schemaVersion": 1,
        }
        event["entryHash"] = verifier.compute_entry_hash(event)
        prev_hash = event["entryHash"]
        events.append(event)
    return events


class TimestampReformatTests(unittest.TestCase):
    def test_preserves_seven_digit_fraction_and_utc_offset(self):
        self.assertEqual(
            verifier.to_dotnet_round_trip_utc("2026-08-10T09:14:00.1234567+00:00"),
            "2026-08-10T09:14:00.1234567+00:00",
        )

    def test_normalizes_z_suffix(self):
        self.assertEqual(
            verifier.to_dotnet_round_trip_utc("2026-08-10T09:14:00Z"),
            "2026-08-10T09:14:00.0000000+00:00",
        )

    def test_pads_short_fraction(self):
        self.assertEqual(
            verifier.to_dotnet_round_trip_utc("2026-08-10T09:14:00.5Z"),
            "2026-08-10T09:14:00.5000000+00:00",
        )

    def test_shifts_nonzero_offset_to_utc_without_losing_fraction(self):
        # 09:14:00+05:00 -> 04:14:00Z; the 7th-digit fractional value must survive the shift.
        self.assertEqual(
            verifier.to_dotnet_round_trip_utc("2026-08-10T09:14:00.1234567+05:00"),
            "2026-08-10T04:14:00.1234567+00:00",
        )


class VerifyChainTests(unittest.TestCase):
    def test_valid_chain_passes(self):
        events = make_chain(3)
        self.assertEqual(verifier.verify(events), [])

    def test_tampered_field_is_detected_at_its_seq(self):
        events = make_chain(3)
        events[1]["actorIdentity"] = "attacker@example.com"  # detail changed after hashing

        findings = verifier.verify(events)

        self.assertEqual(len(findings), 1)
        self.assertIn(f"Seq {events[1]['seq']}", findings[0])
        self.assertIn("EntryHash mismatch", findings[0])

    def test_reordered_events_are_detected(self):
        events = make_chain(3)
        events[0], events[1] = events[1], events[0]

        findings = verifier.verify(events)

        self.assertTrue(any("out of order" in f for f in findings))

    def test_duplicated_seq_is_detected(self):
        events = make_chain(3)
        duplicate = dict(events[0])
        events.insert(1, duplicate)

        findings = verifier.verify(events)

        self.assertTrue(any("duplicated" in f for f in findings))

    def test_broken_adjacent_link_is_detected(self):
        events = make_chain(3)
        events[2]["prevHash"] = "a" * 64
        events[2]["entryHash"] = verifier.compute_entry_hash(events[2])

        findings = verifier.verify(events)

        self.assertTrue(any("adjacent" in f for f in findings))

    def test_truncated_export_detected_against_manifest(self):
        events = make_chain(4)
        manifest = {
            "operationId": OPERATION_ID,
            "chain": {"firstSeq": events[0]["seq"], "lastSeq": events[-1]["seq"]},
        }
        truncated = events[:-1]  # drop the last event without updating the manifest

        findings = verifier.verify(truncated, manifest)

        self.assertTrue(any("truncation" in f for f in findings))

    def test_forged_genesis_prevhash_on_a_non_first_event_is_detected(self):
        events = make_chain(2)
        events[1]["prevHash"] = verifier.GENESIS_HASH  # forged as if it were the very first event
        events[1]["entryHash"] = verifier.compute_entry_hash(events[1])

        findings = verifier.verify(events)

        self.assertTrue(any("genesis" in f for f in findings))

    def test_empty_export_is_a_finding_not_a_crash(self):
        self.assertEqual(verifier.verify([]), ["No events to verify — an empty export cannot be checked."])


class LoadExportTests(unittest.TestCase):
    def test_loads_raw_events_array(self):
        events = make_chain(2)
        with tempfile.NamedTemporaryFile(mode="w", suffix=".json", delete=False) as f:
            json.dump(events, f)
            path = f.name
        try:
            loaded_events, manifest = verifier.load_export(path)
            self.assertEqual(len(loaded_events), 2)
            self.assertIsNone(manifest)
        finally:
            os.unlink(path)

    def test_loads_bundle_with_manifest(self):
        events = make_chain(2)
        bundle = {"manifest": {"operationId": OPERATION_ID}, "operation": {}, "entries": [], "events": events}
        with tempfile.NamedTemporaryFile(mode="w", suffix=".json", delete=False) as f:
            json.dump(bundle, f)
            path = f.name
        try:
            loaded_events, manifest = verifier.load_export(path)
            self.assertEqual(len(loaded_events), 2)
            self.assertEqual(manifest["operationId"], OPERATION_ID)
        finally:
            os.unlink(path)

    def test_loads_package_zip(self):
        events = make_chain(2)
        manifest = {"operationId": OPERATION_ID, "chain": {"firstSeq": 1, "lastSeq": 2}}
        with tempfile.NamedTemporaryFile(suffix=".zip", delete=False) as f:
            path = f.name
        try:
            with zipfile.ZipFile(path, "w") as archive:
                archive.writestr("events.json", json.dumps(events))
                archive.writestr("manifest.json", json.dumps(manifest))
                archive.writestr("operation.json", "{}")
                archive.writestr("entries.json", "[]")
                archive.writestr("entries.csv", "")

            loaded_events, loaded_manifest = verifier.load_export(path)
            self.assertEqual(len(loaded_events), 2)
            self.assertEqual(loaded_manifest["operationId"], OPERATION_ID)

            findings = verifier.verify(loaded_events, loaded_manifest)
            self.assertEqual(findings, [])
        finally:
            os.unlink(path)

    def test_zip_missing_events_json_is_a_load_error(self):
        with tempfile.NamedTemporaryFile(suffix=".zip", delete=False) as f:
            path = f.name
        try:
            with zipfile.ZipFile(path, "w") as archive:
                archive.writestr("manifest.json", "{}")

            with self.assertRaises(verifier.VerificationError):
                verifier.load_export(path)
        finally:
            os.unlink(path)


def make_archive(epoch_number, events):
    """Builds a well-formed epoch-archive document (roadmap next-chapter M5.2) around a fixture
    chain from make_chain(), matching exactly what RecoveryEpochArchiveService writes."""
    return {
        "ownerId": OWNER_ID,
        "epochNumber": epoch_number,
        "startSeq": events[0]["seq"],
        "startPrevHash": events[0]["prevHash"],
        "endSeq": events[-1]["seq"],
        "terminalHash": events[-1]["entryHash"],
        "sealedAtUtc": "2026-09-06T00:00:00.0000000+00:00",
        "events": events,
    }


class ArchiveChainTests(unittest.TestCase):
    def _write_archive(self, directory, epoch_number, events):
        path = os.path.join(directory, f"epoch-{epoch_number:06d}.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(make_archive(epoch_number, events), f)
        return path

    def test_single_valid_archive_verifies_alone(self):
        events = make_chain(3)
        with tempfile.TemporaryDirectory() as tmp:
            self._write_archive(tmp, 1, events)
            findings, last_archive = verifier.verify_archive_chain(tmp)
            self.assertEqual(findings, [])
            self.assertEqual(last_archive["endSeq"], events[-1]["seq"])

    def test_two_archives_chain_correctly(self):
        epoch1_events = make_chain(3, start_seq=1)
        epoch2_events = make_chain(2, start_seq=epoch1_events[-1]["seq"] + 1)
        epoch2_events[0]["prevHash"] = epoch1_events[-1]["entryHash"]
        epoch2_events[0]["entryHash"] = verifier.compute_entry_hash(epoch2_events[0])
        # Re-chain the rest of epoch 2 from the corrected first event.
        prev_hash = epoch2_events[0]["entryHash"]
        for evt in epoch2_events[1:]:
            evt["prevHash"] = prev_hash
            evt["entryHash"] = verifier.compute_entry_hash(evt)
            prev_hash = evt["entryHash"]

        with tempfile.TemporaryDirectory() as tmp:
            self._write_archive(tmp, 1, epoch1_events)
            self._write_archive(tmp, 2, epoch2_events)
            findings, last_archive = verifier.verify_archive_chain(tmp)
            self.assertEqual(findings, [])
            self.assertEqual(last_archive["endSeq"], epoch2_events[-1]["seq"])

    def test_broken_link_between_archives_is_detected(self):
        epoch1_events = make_chain(3, start_seq=1)
        epoch2_events = make_chain(2, start_seq=epoch1_events[-1]["seq"] + 1)
        # Deliberately leave epoch2's startPrevHash NOT matching epoch1's terminal hash.

        with tempfile.TemporaryDirectory() as tmp:
            self._write_archive(tmp, 1, epoch1_events)
            self._write_archive(tmp, 2, epoch2_events)
            findings, _ = verifier.verify_archive_chain(tmp)
            self.assertTrue(any("do not chain to each other" in f for f in findings))

    def test_no_archive_files_is_a_finding_not_a_crash(self):
        with tempfile.TemporaryDirectory() as tmp:
            findings, last_archive = verifier.verify_archive_chain(tmp)
            self.assertTrue(any("No epoch-*.json archive files found" in f for f in findings))
            self.assertIsNone(last_archive)

    def test_cli_follows_archive_into_live_export(self):
        epoch1_events = make_chain(3, start_seq=1)
        live_events = make_chain(2, start_seq=epoch1_events[-1]["seq"] + 1)
        live_events[0]["prevHash"] = epoch1_events[-1]["entryHash"]
        live_events[0]["entryHash"] = verifier.compute_entry_hash(live_events[0])
        prev_hash = live_events[0]["entryHash"]
        for evt in live_events[1:]:
            evt["prevHash"] = prev_hash
            evt["entryHash"] = verifier.compute_entry_hash(evt)
            prev_hash = evt["entryHash"]

        with tempfile.TemporaryDirectory() as tmp:
            self._write_archive(tmp, 1, epoch1_events)
            live_path = os.path.join(tmp, "events.json")
            with open(live_path, "w", encoding="utf-8") as f:
                json.dump(live_events, f)

            old_argv = sys.argv
            sys.argv = ["verify-recovery-chain.py", live_path, "--archive-dir", tmp]
            try:
                self.assertEqual(verifier.main(), 0)
            finally:
                sys.argv = old_argv

    def test_cli_detects_live_export_not_continuing_from_archive(self):
        epoch1_events = make_chain(3, start_seq=1)
        # Live export starts fresh at Seq 1 again instead of continuing from the archive.
        live_events = make_chain(2, start_seq=1)

        with tempfile.TemporaryDirectory() as tmp:
            self._write_archive(tmp, 1, epoch1_events)
            live_path = os.path.join(tmp, "events.json")
            with open(live_path, "w", encoding="utf-8") as f:
                json.dump(live_events, f)

            old_argv = sys.argv
            sys.argv = ["verify-recovery-chain.py", live_path, "--archive-dir", tmp]
            try:
                self.assertEqual(verifier.main(), 1)
            finally:
                sys.argv = old_argv


class MainCliTests(unittest.TestCase):
    def _run_main(self, path):
        old_argv = sys.argv
        sys.argv = ["verify-recovery-chain.py", path]
        try:
            return verifier.main()
        finally:
            sys.argv = old_argv

    def test_valid_export_exits_zero(self):
        events = make_chain(3)
        with tempfile.NamedTemporaryFile(mode="w", suffix=".json", delete=False) as f:
            json.dump(events, f)
            path = f.name
        try:
            self.assertEqual(self._run_main(path), 0)
        finally:
            os.unlink(path)

    def test_tampered_export_exits_one(self):
        events = make_chain(3)
        events[1]["detailJson"] = json.dumps({"reason": "forged"})
        with tempfile.NamedTemporaryFile(mode="w", suffix=".json", delete=False) as f:
            json.dump(events, f)
            path = f.name
        try:
            self.assertEqual(self._run_main(path), 1)
        finally:
            os.unlink(path)

    def test_missing_file_exits_two(self):
        self.assertEqual(self._run_main("/nonexistent/path/events.json"), 2)


if __name__ == "__main__":
    unittest.main()
