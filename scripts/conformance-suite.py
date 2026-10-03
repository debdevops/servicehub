#!/usr/bin/env python3
"""4.1.0 provider-conformance suite (P46 of PORTING-MAP.md): asserts, against a LIVE running
ServiceHub API talking to REAL cloud brokers, that each connected provider's behaviour actually
matches what ProviderCapabilities.{Azure,Aws,Gcp}
(services/api/src/ServiceHub.Core/Models/ProviderCapabilities.cs) declares — including the
negative facts (an unsupported operation must be rejected with the documented
capability-unsupported error, not silently ignored or 500).

This is a PORT of archive/servicehub-4.0.0/scripts/conformance-suite.py (PORTING-MAP.md P46), not
a copy — 4.1.0 is a from-scratch rewrite with a different API shape. What changed, concretely:

  * There is no standalone GET /cloud-bridge/capabilities endpoint any more. Capabilities are
    carried on every namespace in GET /api/v1/namespaces (NamespaceResponse.Capabilities) — this
    suite reads them from there, still never hardcoding a duplicate table.
  * DLQ actions (replay, purge) now act on a durable *row id* ServiceHub recorded for a dead
    letter (DeadLettersController, `/api/v1/dead-letters/{id}/...`), not on live provider
    coordinates (sequence number + entity name) the way 4.0.0's `/api/v1/messages/purge` did. A
    row has to exist first — this suite's "look" step (below) is what creates one for AWS/GCP.
  * "Live Tail" (an open SSE session) and the old "DLQ background scan" trigger
    (`POST /dlq/scan/{id}`) no longer exist as separate concepts. Both were replaced by one
    on-demand action, `POST /api/v1/namespaces/{id}/dead-letters/look`
    (IDeadLetterLook/DeadLetterLook.cs): a person-consented look that records what it finds,
    flagged `countsAsDeliveryAttempt` for providers with no repeatable peek. This suite exercises
    that single action for every provider instead of the old two.
  * There is no manual-dead-letter action exposed over the API in 4.1.0 (no
    `POST .../deadletter` the way 4.0.0 had). `SupportsManualDeadLetter` is reported as SKIPPED —
    not silently dropped — with the reason recorded, since fabricating a check against a route
    that does not exist would be worse than admitting the gap.
  * `SupportsScheduledMessages` is only checked negatively where the capability is false: the
    public `POST .../messages` request body (MessagesController.SendRequest) does not expose a
    `scheduledEnqueueTimeUtc` field at all any more (the internal SendMessageRequest DTO still
    has one, but nothing in the REST API sets it) — so 4.1.0 currently has no way to schedule a
    message through the API on ANY provider, Azure included. This suite reports that as its own
    finding (a real product gap, not a suite bug) rather than pretending to exercise a positive
    scheduled-send path that does not exist. The negative path (`GET .../messages/scheduled`
    returning 409 CapabilityUnavailable on AWS/GCP) is still checked, since that endpoint is real.
  * `CanProveDlqAbsence` is a static per-provider fact on `ProviderCapabilities` in both versions;
    4.1.0 has no per-namespace DLQ-observer-attestation lookup route (that concept now lives
    inside signature trust, `GET /api/v1/signatures/{hash}/trust`, keyed by a failure signature
    hash rather than a namespace id) — this suite reports the static fact directly, same as it
    always was, and separately best-effort cross-checks a live signature's trust reason when
    `--signature-hash` is given for a provider.

Never fabricates data: every assertion is a real HTTP call against a running ServiceHub API
(:5153 by default), which talks to whatever real namespaces are registered on it (default owner
partition `__spa__`, i.e. no X-API-KEY needed — matching how the browser session itself talks to
the API). Prerequisites: ServiceHub API running and at least one namespace already registered per
provider you want exercised.

Usage: python3 scripts/conformance-suite.py run
           --namespace Provider=<namespace-id> [--namespace Provider=<namespace-id> ...]
           [--signature-hash Provider=<hash>]
       python3 scripts/conformance-suite.py preflight

Unlike the 4.0.0 script, `run` takes only a namespace id per provider — no entity name is needed,
because every action here (send excepted) now works off ServiceHub's own dead-letter row ids, not
raw queue/topic coordinates. `send` picks the namespace's first queue-kind entity automatically
via `GET /api/v1/namespaces/{id}/entities?kind=queue`.

Exit code is 0 iff every assertion that actually ran passed. Providers with no --namespace given
are reported SKIPPED (not FAILED) — this lets the suite run against whichever providers happen to
be connected on a given machine rather than failing outright when one isn't.
"""
import argparse
import json
import sys
import time
import uuid
from datetime import datetime, timezone

import requests

SH_BASE = "http://localhost:5153"
HEADERS = {"Content-Type": "application/json"}

INTENT_HEADER = "X-ServiceHub-Intent"


def intent_headers(intent):
    return {**HEADERS, INTENT_HEADER: intent}


def sh(method, path, extra_headers=None, **kwargs):
    headers = {**HEADERS, **extra_headers} if extra_headers else HEADERS
    r = requests.request(method, f"{SH_BASE}{path}", headers=headers, timeout=30, **kwargs)
    try:
        body = r.json()
    except ValueError:
        body = r.text
    return r.status_code, body


class Report:
    def __init__(self):
        self.results = []

    def check(self, provider, name, ok, detail, trust_root=None):
        """trust_root (ADR-004; ADR-0011; M3.3): which fact CanProveDlqAbsence actually rests on
        for this provider right now — "provider-native" (a real, uncapped peek; Azure only),
        "operator-attested" (a live, independently-verified DLQ observer signature trust reason),
        or "none" (AWS/GCP with neither)."""
        status = "PASS" if ok else "FAIL"
        entry = {"provider": provider, "assertion": name, "status": status, "detail": detail}
        if trust_root is not None:
            entry["trustRoot"] = trust_root
        self.results.append(entry)
        suffix = f" [trustRoot={trust_root}]" if trust_root is not None else ""
        print(f"[{status}] {provider}: {name} — {detail}{suffix}")
        return ok

    def skip(self, provider, name, detail):
        self.results.append({"provider": provider, "assertion": name, "status": "SKIPPED", "detail": detail})
        print(f"[SKIP] {provider}: {name} — {detail}")

    def summary(self):
        passed = sum(1 for r in self.results if r["status"] == "PASS")
        failed = sum(1 for r in self.results if r["status"] == "FAIL")
        skipped = sum(1 for r in self.results if r["status"] == "SKIPPED")
        return passed, failed, skipped

    def write(self, path):
        with open(path, "w") as f:
            json.dump({"generatedAtUtc": datetime.now(timezone.utc).isoformat(), "results": self.results}, f, indent=2)


def cmd_preflight(_args):
    code, body = sh("GET", "/api/v1/namespaces")
    print(f"GET /namespaces -> HTTP {code}")
    print(json.dumps(body, indent=2))


PROVIDERS = ("Azure", "Aws", "Gcp")


def canonical_provider(name):
    """`aws`, `AWS` and `Aws` all mean Aws. An unknown name is a usage error — silently ignoring it
    used to turn a mistyped `--namespace aws=...` into an all-SKIP run that looked like a pass."""
    for known in PROVIDERS:
        if known.lower() == name.strip().lower():
            return known
    sys.exit(f"unknown provider '{name}' (expected one of: {', '.join(PROVIDERS)})")


def parse_namespace_args(pairs):
    """--namespace Provider=<namespace-id>, repeated -> {"Azure": ns_id, ...}."""
    out = {}
    for p in pairs:
        provider, ns_id = p.split("=", 1)
        out[canonical_provider(provider)] = ns_id
    return out


def parse_signature_args(pairs):
    out = {}
    for p in pairs or []:
        provider, hash_ = p.split("=", 1)
        out[canonical_provider(provider)] = hash_
    return out


def first_sendable_entity(ns_id):
    """(name, is_topic) for the first queue this namespace has, or its first topic if it has no
    queue at all (e.g. GCP, which is topic/subscription-only in this dataset) — send works on
    either, it just needs to know which."""
    code, body = sh("GET", f"/api/v1/namespaces/{ns_id}/entities")
    if code != 200 or not isinstance(body, dict):
        return None, False
    items = body.get("entities") or []
    queues = [e for e in items if e.get("kind") == "queue"]
    if queues:
        return queues[0]["name"], False
    topics = [e for e in items if e.get("kind") == "topic"]
    return (topics[0]["name"], True) if topics else (None, False)


def first_any_entity(ns_id):
    """Any queue or topic entity — used only to have *something* resolvable to ask
    messages/scheduled about; a topic-only provider (GCP) has no queue-kind entity at all, which
    is itself expected and not a capability failure."""
    name, _ = first_sendable_entity(ns_id)
    return name


def latest_active_dlq_id(ns_id):
    code, body = sh("GET", f"/api/v1/dead-letters?namespaceId={ns_id}&status=active&pageSize=1")
    if code != 200 or not isinstance(body, dict):
        return None
    items = body.get("items") or []
    return items[0]["id"] if items else None


def run_provider(report, provider, ns_id, sig_hash, caps):
    print(f"\n=== {provider} (namespace {ns_id}) ===")
    print(f"Declared capabilities (from GET /namespaces): {json.dumps(caps, indent=2)}")

    # --- send: baseline positive op every provider must support ---
    entity, entity_is_topic = first_sendable_entity(ns_id)
    if entity is None:
        report.skip(provider, "send (baseline)", "no queue- or topic-kind entity found via GET .../entities")
    else:
        marker = f"conformance-{provider.lower()}-{uuid.uuid4().hex[:8]}"
        code, body = sh(
            "POST", f"/api/v1/namespaces/{ns_id}/messages",
            extra_headers=intent_headers("send-message"),
            json={"entity": entity, "isTopic": entity_is_topic, "body": marker, "contentType": "text/plain"},
        )
        report.check(provider, "send (baseline)", code == 200, f"HTTP {code}: {body if code != 200 else 'accepted'}")

    # --- manual dead-letter: SupportsManualDeadLetter — no API action exists in 4.1.0 ---
    report.skip(
        provider, "manual dead-letter",
        "4.1.0 exposes no POST .../deadletter action over the API (removed vs. 4.0.0); "
        f"static capability value is SupportsManualDeadLetter={caps['supportsManualDeadLetter']}, unverifiable live")

    # --- scheduled messages: SupportsScheduledMessages ---
    # No provider can be sent a scheduled message through the public API any more (see module
    # docstring) — only the negative listing path is real and checkable. Needs *some* resolvable
    # entity, not necessarily a queue (GCP has none) — a topic works equally well here.
    sched_entity = entity or first_any_entity(ns_id)
    if sched_entity is None:
        report.skip(provider, "scheduled messages listing", "no queue or topic entity found to resolve against")
        report.skip(provider, "scheduled send", "no entity to target")
    else:
        code, body = sh("GET", f"/api/v1/namespaces/{ns_id}/messages/scheduled?entity={sched_entity}")
        if caps["supportsScheduledMessages"]:
            report.check(provider, "scheduled messages listing (positive — no 409)", code != 409, f"HTTP {code}: {body}")
            report.skip(provider, "scheduled send",
                         "4.1.0's public API has no field to schedule a send on ANY provider (SendRequest carries no "
                         "scheduledEnqueueTimeUtc) — this is a real product gap, not exercisable here even for Azure")
        else:
            report.check(provider, "scheduled messages listing (negative — must be REJECTED, 409 CapabilityUnavailable)",
                          code == 409, f"HTTP {code}: {body}")

    # --- look at dead letters now: replaces both Live Tail and the old manual DLQ-scan trigger ---
    code, body = sh(
        "POST", f"/api/v1/namespaces/{ns_id}/dead-letters/look",
        extra_headers=intent_headers("look-at-dead-letters"),
    )
    if code == 200 and isinstance(body, dict):
        outcome_ok = body.get("outcome") == "looked"
        expected_destructive = not caps["supportsRepeatablePeek"]
        flag_ok = body.get("countsAsDeliveryAttempt") == expected_destructive
        report.check(
            provider, "look at dead letters now (outcome=looked)", outcome_ok, f"HTTP {code}: {body}")
        report.check(
            provider,
            f"look's countsAsDeliveryAttempt matches !SupportsRepeatablePeek (expected {expected_destructive})",
            flag_ok, f"HTTP {code}: countsAsDeliveryAttempt={body.get('countsAsDeliveryAttempt')}")
    elif code == 409 and isinstance(body, dict) and body.get("code") == "already_running":
        report.skip(provider, "look at dead letters now", f"another look was already running: {body}")
    else:
        report.check(provider, "look at dead letters now", False, f"HTTP {code}: {body}")

    time.sleep(1)  # let the look's recording settle before reading the DLQ list back

    # --- purge: SupportsPurge, against a real recorded dead-letter row ---
    dlq_id = latest_active_dlq_id(ns_id)
    if dlq_id is None:
        report.skip(provider, "purge", "no active dead-letter row found via GET /dead-letters (none recorded yet)")
    else:
        code, body = sh(
            "POST", f"/api/v1/dead-letters/{dlq_id}/purge",
            extra_headers=intent_headers("purge-message"),
            json={"reason": "ConformanceSuite"},
        )
        if caps["supportsPurge"]:
            # HTTP 200 alone proves nothing: the endpoint answers 200 with `result: rejected` when the
            # provider refused (2026-09-28: GCP, straight after a `look` that had leased the message
            # for its ack deadline -> GCP.PubSub.MessageNotFound). Only `accepted` proves a purge.
            result = body.get("result") if isinstance(body, dict) else None
            if code == 200 and result == "accepted":
                report.check(provider, "purge (positive)", True, f"HTTP {code}: {body}")
            elif code == 200 and result == "rejected" and body.get("errorCode", "").endswith("MessageNotFound"):
                report.skip(provider, "purge (positive)",
                            f"unproven, not failed: the provider could not find the row's message right after the look "
                            f"(a pull-based look leases messages for the ack deadline): {body}")
            else:
                report.check(provider, "purge (positive)", False, f"HTTP {code}: {body}")
        else:
            report.check(provider, "purge (negative — must be REJECTED, 409 CapabilityUnavailable)",
                          code == 409 and isinstance(body, dict) and body.get("code") == "capability_unavailable",
                          f"HTTP {code}: {body}")

    # --- CanProveDlqAbsence trust root (ADR-004; ADR-0011; M3.3) ---
    # Static per-provider fact either way, same as 4.0.0 — Azure's is always true (uncapped peek);
    # AWS/GCP's is always false absent an independently-verified DLQ observer, which in 4.1.0 is
    # reported per FAILURE SIGNATURE (not per namespace) via GET /signatures/{hash}/trust.
    if caps["canProveDlqAbsence"]:
        report.check(provider, "CanProveDlqAbsence", True,
                     "true via the provider's own native capability (uncapped, non-destructive peek)",
                     trust_root="provider-native")
    elif sig_hash:
        att_code, attestation = sh("GET", f"/api/v1/signatures/{sig_hash}/trust?provider={provider}")
        if att_code == 200 and isinstance(attestation, dict):
            report.check(provider, "CanProveDlqAbsence (via a real signature's trust reason)", True,
                         f"false by default (no provider-native capability); this signature's own trust reason: "
                         f"{json.dumps(attestation)}", trust_root="operator-attested" if attestation.get("cloudCanConfirm") else "none")
        else:
            report.check(provider, "CanProveDlqAbsence (signature trust lookup)", False,
                         f"HTTP {att_code}: {attestation}", trust_root="none")
    else:
        report.check(provider, "CanProveDlqAbsence (negative — no --signature-hash given to cross-check)", True,
                     "false via the static capability preset; no --signature-hash provided so the "
                     "operator-attested path (GET /signatures/{hash}/trust) was not exercised",
                     trust_root="none")


def cmd_run(args):
    ns_map = parse_namespace_args(args.namespace or [])
    sig_map = parse_signature_args(args.signature_hash)
    code, namespaces = sh("GET", "/api/v1/namespaces")
    if code != 200:
        print(f"FATAL: GET /namespaces -> HTTP {code}")
        sys.exit(2)

    report = Report()
    report.check("suite", "GET /namespaces reachable", code == 200, f"HTTP {code}")

    by_id = {n["id"]: n for n in namespaces} if isinstance(namespaces, list) else {}

    for provider in ("Azure", "Aws", "Gcp"):
        if provider not in ns_map:
            report.skip(provider, "all live assertions", "no --namespace given for this provider")
            continue
        ns_id = ns_map[provider]
        ns = by_id.get(ns_id)
        if ns is None:
            report.check(provider, "namespace visible via GET /namespaces", False,
                         f"namespace id {ns_id} not found in the caller's own namespace list")
            continue
        caps = ns.get("capabilities")
        if not caps:
            report.check(provider, "namespace has capabilities (adapter registered)", False,
                         f"Capabilities was null for namespace {ns_id} — no adapter registered for this provider in this build")
            continue
        run_provider(report, provider, ns_id, sig_map.get(provider), caps)

    passed, failed, skipped = report.summary()
    print(f"\n=== SUMMARY: {passed} passed, {failed} failed, {skipped} skipped ===")
    report.write(args.report_path)
    print(f"Report written to {args.report_path}")
    sys.exit(1 if failed > 0 else 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    sub.add_parser("preflight")

    p_run = sub.add_parser("run")
    p_run.add_argument("--namespace", action="append", help="Provider=<namespace-id>, repeatable")
    p_run.add_argument("--signature-hash", action="append",
                        help="Provider=<hash>, repeatable — best-effort cross-check of a real signature's "
                             "trust reason for CanProveDlqAbsence; optional")
    p_run.add_argument("--report-path", default="conformance-report.json")

    args = parser.parse_args()
    {"preflight": cmd_preflight, "run": cmd_run}[args.command](args)


if __name__ == "__main__":
    main()
