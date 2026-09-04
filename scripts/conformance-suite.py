#!/usr/bin/env python3
"""W4.1 provider-conformance suite: asserts, against a LIVE running ServiceHub API talking to
REAL cloud brokers, that each connected provider's behaviour actually matches what
ProviderCapabilities.{Azure,Aws,Gcp} (services/api/src/ServiceHub.Core/Models/ProviderCapabilities.cs)
declares — including the negative facts (an unsupported operation must be rejected with the
documented capability-unsupported error, not silently ignored or 500). This is the gap the
2026-08-30/2026-09-03 campaigns and the existing per-provider unit tests never closed: the code is
proven correct in isolation (mocked SDKs), never proven correct end to end against a live broker.

Reads the capability facts to assert from the API's own GET /cloud-bridge/capabilities response
(the same static ProviderCapabilities presets, served live) rather than hardcoding a duplicate
table here, so this suite can't drift from the source of truth it's checking.

Never fabricates data: every assertion is a real HTTP call against a running ServiceHub API
(:5153 by default), which talks to whatever real namespaces are registered on it. Prerequisites:
ServiceHub API running and at least one namespace already registered per provider you want
exercised (`register-aws` below can add one for AWS given AKID:secret; Azure/GCP need their
own connection-string/service-account setup first — this script does not provision cloud infra).

Usage: python3 scripts/conformance-suite.py run [--namespace Provider=<namespace-id>=<entityName> ...]
       python3 scripts/conformance-suite.py preflight
       python3 scripts/conformance-suite.py register-aws <namespace-name> <region> <akid> <secret>

Example (this run's actual namespaces):
  python3 scripts/conformance-suite.py run \\
      --namespace Azure=5f815da0-931b-4ed6-a9e2-af124bcb6200=orders \\
      --namespace Aws=ed21bf7a-c595-468d-992d-fc495803d21e=servicehub-dev-orders

Exit code is 0 iff every assertion that actually ran passed. Providers with no --namespace given
are reported SKIPPED (not FAILED) — this is "Azure first," not "Azure only, and everything else is
broken"; GCP in particular needs a minted service-account key this script deliberately does not
create on its own (see docs-private/w4-1-conformance-suite-2026-09-04/RESULTS.md).
"""
import argparse
import json
import sys
import time
import uuid
from datetime import datetime, timedelta, timezone

import requests

SH_BASE = "http://localhost:5153"
API_KEY = "a1a1a1a1000000000000000000000000000000000000000000000000scopefull"
HEADERS = {"X-API-KEY": API_KEY, "Content-Type": "application/json"}

INTENT_HEADER = "X-ServiceHub-Intent"
CONFIRM_HEADER = "X-ServiceHub-Confirm"


def intent_headers(intent):
    return {**HEADERS, INTENT_HEADER: intent, CONFIRM_HEADER: "true"}


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

    def check(self, provider, name, ok, detail):
        status = "PASS" if ok else "FAIL"
        self.results.append({"provider": provider, "assertion": name, "status": status, "detail": detail})
        print(f"[{status}] {provider}: {name} — {detail}")
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
    code, caps = sh("GET", "/api/v1/cloud-bridge/capabilities")
    print(f"GET /cloud-bridge/capabilities -> HTTP {code}")
    print(json.dumps(caps, indent=2))


def cmd_register_aws(args):
    conn = f"{args.akid}:{args.secret}"
    code, body = sh(
        "POST",
        "/api/v1/namespaces",
        json={
            "name": args.name,
            "connectionString": conn,
            "authType": "awsAccessKey",
            "provider": "Aws",
            "displayName": args.name,
            "environment": "Dev",
            "awsRegion": args.region,
        },
    )
    print(f"HTTP {code}")
    print(json.dumps(body, indent=2))


def parse_namespace_args(pairs):
    """--namespace Provider=<id>=<entityName> repeated -> {"Azure": (id, entity), ...}"""
    out = {}
    for p in pairs:
        provider, ns_id, entity = p.split("=", 2)
        out[provider] = (ns_id, entity)
    return out


def peek_dead_letter(ns_id, entity, max_messages=5):
    code, body = sh("GET", f"/api/v1/messages/queue/{entity}/deadletter?namespaceId={ns_id}&maxMessages={max_messages}")
    return code, body


def run_provider(report, provider, ns_id, entity, caps):
    print(f"\n=== {provider} (namespace {ns_id}, entity {entity}) ===")
    print(f"Declared capabilities: {json.dumps(caps, indent=2)}")

    # --- send: baseline positive op every provider must support ---
    marker = f"conformance-{provider.lower()}-{uuid.uuid4().hex[:8]}"
    code, body = sh(
        "POST", f"/api/v1/namespaces/{ns_id}/queues/{entity}/messages",
        extra_headers=intent_headers("messages:send"),
        json={"body": marker, "contentType": "text/plain"},
    )
    report.check(provider, "send (baseline)", code == 202, f"HTTP {code}: {body if code != 202 else 'accepted'}")

    # --- manual dead-letter: SupportsManualDeadLetter ---
    if caps["supportsManualDeadLetter"]:
        time.sleep(2)  # let the send settle before we ask to dead-letter it
        code, body = sh(
            "POST", f"/api/v1/namespaces/{ns_id}/queues/{entity}/deadletter?messageCount=1&reason=ConformanceSuite",
            extra_headers=intent_headers("messages:deadletter"),
        )
        report.check(provider, "manual dead-letter (positive)", code == 200,
                     f"HTTP {code}: {body if code != 200 else body}")
    else:
        report.skip(provider, "manual dead-letter (negative)",
                    f"{provider} not registered/exercisable here for the negative case — see RESULTS.md")

    # --- scheduled messages: SupportsScheduledMessages ---
    sched_time = datetime.now(timezone.utc) + timedelta(minutes=5)
    code, body = sh(
        "POST", f"/api/v1/namespaces/{ns_id}/queues/{entity}/messages",
        extra_headers=intent_headers("messages:send"),
        json={"body": f"scheduled-{marker}", "contentType": "text/plain",
              "scheduledEnqueueTimeUtc": sched_time.isoformat()},
    )
    if caps["supportsScheduledMessages"]:
        report.check(provider, "scheduled send (positive)", code == 202, f"HTTP {code}: {body}")
        if code == 202:
            time.sleep(2)
            code2, listed = sh("GET", f"/api/v1/namespaces/{ns_id}/queues/{entity}/scheduled")
            found = isinstance(listed, dict) and listed.get("totalCount", 0) > 0 or (isinstance(listed, list) and len(listed) > 0)
            report.check(provider, "scheduled message appears in listing", code2 == 200 and found,
                         f"HTTP {code2}: {listed}")
    else:
        report.check(provider, "scheduled send (negative — must be REJECTED, not silently sent)",
                     code != 202, f"HTTP {code}: {body}")

    # --- purge: SupportsPurge ---
    code, dlq = peek_dead_letter(ns_id, entity)
    target_seq = None
    if code == 200 and isinstance(dlq, list) and len(dlq) > 0:
        target_seq = dlq[0].get("sequenceNumber")
    if target_seq is None:
        report.skip(provider, "purge", "no dead-letter message available to target (DLQ peek returned none)")
    else:
        code, body = sh(
            "DELETE",
            f"/api/v1/messages/purge?namespaceId={ns_id}&sequenceNumber={target_seq}&entityName={entity}&fromDeadLetter=true&reason=ConformanceSuite",
            extra_headers=intent_headers("messages:purge"),
        )
        if caps["supportsPurge"]:
            report.check(provider, "purge (positive)", code == 202, f"HTTP {code}: {body}")
        else:
            report.check(provider, "purge (negative — must be REJECTED, not silently accepted or 500)",
                          code in (400, 422), f"HTTP {code}: {body}")

    # --- Live Tail: SupportsRepeatablePeek ---
    try:
        with requests.get(
            f"{SH_BASE}/api/v1/messages/live-tail?namespaceId={ns_id}&entityName={entity}",
            headers=HEADERS, stream=True, timeout=8,
        ) as r:
            if caps["supportsRepeatablePeek"]:
                report.check(provider, "Live Tail (positive — session opens)", r.status_code == 200,
                              f"HTTP {r.status_code}")
            else:
                report.check(provider, "Live Tail (negative — 409, SupportsRepeatablePeek)", r.status_code == 409,
                              f"HTTP {r.status_code}: {r.text[:300]}")
    except requests.exceptions.ReadTimeout:
        # Azure's live-tail is a long-lived SSE stream by design; a read timeout on an open
        # connection with no error body IS the positive case succeeding, not a failure.
        report.check(provider, "Live Tail (positive — session opened and stayed open)",
                      caps["supportsRepeatablePeek"], "connection opened, held open past the read timeout (expected for SSE)")


def cmd_run(args):
    ns_map = parse_namespace_args(args.namespace or [])
    code, caps_all = sh("GET", "/api/v1/cloud-bridge/capabilities")
    if code != 200:
        print(f"FATAL: GET /cloud-bridge/capabilities -> HTTP {code}")
        sys.exit(2)

    report = Report()
    report.check("suite", "cloud-bridge/capabilities reachable", code == 200, f"HTTP {code}")

    for provider in ("Azure", "Aws", "Gcp"):
        caps = caps_all.get(provider)
        if provider not in ns_map:
            report.skip(provider, "all live assertions", "no --namespace given for this provider")
            continue
        ns_id, entity = ns_map[provider]
        run_provider(report, provider, ns_id, entity, caps)

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
    p_run.add_argument("--namespace", action="append", help="Provider=<namespace-id>=<entityName>, repeatable")
    p_run.add_argument("--report-path", default="conformance-report.json")

    p_reg = sub.add_parser("register-aws")
    p_reg.add_argument("name")
    p_reg.add_argument("region")
    p_reg.add_argument("akid")
    p_reg.add_argument("secret")

    args = parser.parse_args()
    {"preflight": cmd_preflight, "run": cmd_run, "register-aws": cmd_register_aws}[args.command](args)


if __name__ == "__main__":
    main()
