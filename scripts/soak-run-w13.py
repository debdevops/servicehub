#!/usr/bin/env python3
"""W1.3 soak-run harness: drives ServiceHub against real cloud dead-letter traffic
(via servicehub-samples) to observe autonomy transitions end to end, then exports and
independently verifies the resulting evidence.

Never imports ServiceHub/samples code and never fabricates data — every action is a
real HTTP call against a running ServiceHub API and a running servicehub-samples API,
which in turn talk to a real cloud broker. This script only orchestrates and observes.

Usage: python3 scripts/soak-run-w13.py <command> [args...]
Commands:
  register-namespace <name> <connection-string>
  flood <namespace-id> <count> [error-type]
  signatures <namespace-id>
  create-rule <namespace-id> <entity-name> <dead-letter-reason>
  toggle-rule <rule-id> <true|false>
  replay-signature <namespace-id> <signature-hash>
  replay-all <rule-id>
  trust <signature-hash>
  autonomy <signature-hash>
  rule-status <rule-id>
  dashboard
  wait-demotion <signature-hash> [timeout-seconds=180] [poll-seconds=5]
  wait-circuit-breaker <rule-id> [timeout-seconds=300] [poll-seconds=15]
  ledger-entries <namespace-id> [limit]
  export <operation-id> <out-path.zip>
  consumer-pause <provider> <entity>
  consumer-resume <provider> <entity>

Recipes (2026-09-04, extending the 2026-09-03 promotion run — see
docs-private/w1.3-soak-run-2026-09-03/RESULTS.md for the mechanism these exploit):

  Demotion (fast path, DlqMonitorService.EvaluateFastDemotionAsync — near-immediate,
  fires on the live DLQ poll cycle, Dlq:PollIntervalSeconds, default 10s; does NOT
  wait for the hourly AutonomyEvaluationWorker sweep):
    1. Start from a signature already at Standing (L4) — reuse one from the promotion
       run, or build one first (create-rule, toggle-rule true, flood, replay-signature,
       poll autonomy until currentLevel==4).
    2. consumer-pause <provider> <entity> — so replayed messages exhaust redelivery and
       land back in the DLQ instead of completing.
    3. Make sure at least 2 messages carrying that exact signature are in the DLQ
       (flood a couple more with the same entity/error-type if needed), then
       replay-signature <namespace-id> <signature-hash>.
    4. wait-demotion <signature-hash> — polls until currentLevel drops to 3 (Approve).
       DlqMonitorService needs to observe 2 consecutive verified Returned dispositions
       for this exact signature; if it times out, replay-signature again once more.
    5. consumer-resume when done, to stop generating further DLQ noise.

  Circuit-breaker trip (AutonomyEvaluationWorker.SweepAutoReplayCircuitBreakersAsync —
  rule-scoped, not signature-scoped, and does NOT require any AutonomyGrant: the
  rules/{id}/replay-all endpoint passes SignatureHash=null to the Eligibility Gate,
  so it bypasses the autonomy-grant check entirely (see RulesController.cs's own
  comment: "autoReplay flag is no longer required for manual Replay All")):
    1. create-rule <namespace-id> <entity-name> <dead-letter-reason> — created enabled.
    2. consumer-pause <provider> <entity> first, so every replay this rule fires
       bounces straight back to the DLQ (Returned), not Recovered.
    3. flood <namespace-id> <count>=~25 <error-type> — comfortably over
       RecoveryEvidence:CircuitBreakerSampleSize (default 20), so replay-all's
       verified-disposition sample is dominated by Returned once these resolve.
    4. replay-all <rule-id> — one call replays every matched Active message,
       each individually eligibility-gate-checked and ledger-recorded with this
       rule's SourceRuleId (RecurrenceLineageCap=3 is per-message-lineage and
       irrelevant here since each flooded message is a fresh lineage).
    5. wait-circuit-breaker <rule-id> — polls GET /dlq/rules/{id} for
       disabledReason=="CircuitBreaker". The sweep only runs every
       RecoveryEvidence:AutonomyEvaluationSweepIntervalSeconds (default 3600s) — as
       in the 2026-09-03 run, override it to something short (e.g. 60s) for this run
       so the wait is minutes, not an hour.
    6. dashboard — sanity-check circuitBreakerTrips/recentTransitions in one call.
       Re-enable the rule afterward (toggle-rule <rule-id> true) to clean up.
"""
import json
import sys
import time

import requests

SH_BASE = "http://localhost:5153"
SAMPLES_BASE = "http://localhost:5280"
API_KEY = "a1a1a1a1000000000000000000000000000000000000000000000000scopefull"
HEADERS = {"X-API-KEY": API_KEY, "Content-Type": "application/json"}

INTENT_HEADER = "X-ServiceHub-Intent"
CONFIRM_HEADER = "X-ServiceHub-Confirm"


def intent_headers(intent):
    """Merges the explicit-intent headers IntentHeaders.HasExplicitIntent requires
    for risky mutating endpoints (signature replay, rule replay-all, ...) on top of
    the base headers. Without these the API returns 428 Precondition Required."""
    return {**HEADERS, INTENT_HEADER: intent, CONFIRM_HEADER: "true"}


def sh(method, path, extra_headers=None, **kwargs):
    headers = {**HEADERS, **extra_headers} if extra_headers else HEADERS
    r = requests.request(method, f"{SH_BASE}{path}", headers=headers, timeout=30, **kwargs)
    try:
        body = r.json()
    except ValueError:
        body = r.text
    return r.status_code, body


def out(status, body):
    print(f"HTTP {status}")
    print(json.dumps(body, indent=2) if isinstance(body, (dict, list)) else body)


def cmd_register_namespace(name, conn):
    status, body = sh(
        "POST",
        "/api/v1/namespaces",
        json={
            "name": name,
            "connectionString": conn,
            "authType": "ConnectionString",
            "displayName": name,
            "environment": "Dev",
        },
    )
    out(status, body)


def cmd_flood(namespace_id, count, error_type="PaymentTimeout"):
    r = requests.post(
        f"{SAMPLES_BASE}/api/scenarios/dlq-flood/start",
        json={"Provider": "azure", "Entity": "orders", "Count": int(count), "RatePerSecond": 5, "ErrorType": error_type},
        timeout=30,
    )
    print(f"HTTP {r.status_code}")
    print(r.text)


def cmd_signatures(namespace_id):
    status, body = sh("GET", f"/api/v1/namespaces/{namespace_id}/dlq/signatures")
    out(status, body)


def cmd_create_rule(namespace_id, entity_name, reason):
    status, body = sh(
        "POST",
        "/api/v1/dlq/rules",
        json={
            "name": f"soak-w13-{entity_name}",
            "description": "W1.3 soak-run: build+observe autonomy trust for one signature",
            "enabled": True,
            "namespaceId": namespace_id,
            "conditions": [
                {"field": "EntityName", "operator": "Equals", "value": entity_name, "caseSensitive": False},
                {"field": "DeadLetterReason", "operator": "Equals", "value": reason, "caseSensitive": False},
            ],
            "action": {"delaySeconds": 0},
            "maxReplaysPerHour": 1000,
        },
    )
    out(status, body)


def cmd_toggle_rule(rule_id, enabled):
    status, body = sh("POST", f"/api/v1/dlq/rules/{rule_id}/toggle", json={"enabled": enabled.lower() == "true"})
    out(status, body)


def cmd_replay_signature(namespace_id, signature_hash):
    # IntentHeaders.HasExplicitIntent gates this endpoint (428 without both headers) —
    # missing here until 2026-09-04; the 2026-09-03 run's 14 manual replays went
    # through the ServiceHub UI instead, which sets these headers itself.
    status, body = sh(
        "POST",
        f"/api/v1/namespaces/{namespace_id}/dlq/signatures/{signature_hash}/replay",
        extra_headers=intent_headers("signature:replay"),
        json={},
    )
    out(status, body)


def cmd_replay_all(rule_id):
    status, body = sh(
        "POST",
        f"/api/v1/dlq/rules/{rule_id}/replay-all",
        extra_headers=intent_headers("rules:replay-all"),
    )
    out(status, body)


def cmd_trust(signature_hash):
    status, body = sh("GET", f"/api/v1/recovery/trust/{signature_hash}?actionKind=Replay")
    out(status, body)


def cmd_autonomy(signature_hash):
    status, body = sh("GET", f"/api/v1/recovery/autonomy/{signature_hash}?actionKind=Replay")
    out(status, body)


def cmd_rule_status(rule_id):
    status, body = sh("GET", f"/api/v1/dlq/rules/{rule_id}")
    out(status, body)


def cmd_dashboard():
    status, body = sh("GET", "/api/v1/recovery/autonomy-dashboard")
    out(status, body)


def cmd_wait_demotion(signature_hash, timeout_seconds="180", poll_seconds="5"):
    timeout_seconds, poll_seconds = int(timeout_seconds), int(poll_seconds)
    deadline = time.time() + timeout_seconds
    while True:
        status, body = sh("GET", f"/api/v1/recovery/autonomy/{signature_hash}?actionKind=Replay")
        level = body.get("currentLevel") if isinstance(body, dict) else None
        print(f"HTTP {status} currentLevel={level} levelLabel={body.get('levelLabel') if isinstance(body, dict) else None}")
        if isinstance(level, int) and level <= 3:
            print(f"Demoted to L{level} — done.")
            return
        if time.time() >= deadline:
            print(f"Timed out after {timeout_seconds}s waiting for demotion below L4.")
            return
        time.sleep(poll_seconds)


def cmd_wait_circuit_breaker(rule_id, timeout_seconds="300", poll_seconds="15"):
    timeout_seconds, poll_seconds = int(timeout_seconds), int(poll_seconds)
    deadline = time.time() + timeout_seconds
    while True:
        status, body = sh("GET", f"/api/v1/dlq/rules/{rule_id}")
        reason = body.get("disabledReason") if isinstance(body, dict) else None
        print(f"HTTP {status} enabled={body.get('enabled') if isinstance(body, dict) else None} disabledReason={reason}")
        if reason == "CircuitBreaker":
            print(f"Circuit breaker tripped: {body.get('disabledReasonDetail')}")
            return
        if time.time() >= deadline:
            print(f"Timed out after {timeout_seconds}s waiting for the circuit breaker to trip.")
            return
        time.sleep(poll_seconds)


def cmd_ledger_entries(namespace_id, limit=50):
    status, body = sh("GET", f"/api/v1/recovery/entries?namespaceId={namespace_id}&limit={limit}")
    out(status, body)


def cmd_export(operation_id, out_path):
    r = requests.get(f"{SH_BASE}/api/v1/recovery/operations/{operation_id}/export?format=package", headers=HEADERS, timeout=30)
    with open(out_path, "wb") as f:
        f.write(r.content)
    print(f"HTTP {r.status_code} -> wrote {len(r.content)} bytes to {out_path}")


def cmd_consumer_pause(provider, entity):
    r = requests.post(f"{SAMPLES_BASE}/api/consumers/{provider}/{entity}/pause", timeout=15)
    print(f"HTTP {r.status_code}: {r.text}")


def cmd_consumer_resume(provider, entity):
    r = requests.post(f"{SAMPLES_BASE}/api/consumers/{provider}/{entity}/resume", timeout=15)
    print(f"HTTP {r.status_code}: {r.text}")


COMMANDS = {
    "register-namespace": cmd_register_namespace,
    "flood": cmd_flood,
    "signatures": cmd_signatures,
    "create-rule": cmd_create_rule,
    "toggle-rule": cmd_toggle_rule,
    "replay-signature": cmd_replay_signature,
    "replay-all": cmd_replay_all,
    "trust": cmd_trust,
    "autonomy": cmd_autonomy,
    "rule-status": cmd_rule_status,
    "dashboard": cmd_dashboard,
    "wait-demotion": cmd_wait_demotion,
    "wait-circuit-breaker": cmd_wait_circuit_breaker,
    "ledger-entries": cmd_ledger_entries,
    "export": cmd_export,
    "consumer-pause": cmd_consumer_pause,
    "consumer-resume": cmd_consumer_resume,
}


def main():
    if len(sys.argv) < 2 or sys.argv[1] not in COMMANDS:
        print(__doc__)
        sys.exit(2)
    COMMANDS[sys.argv[1]](*sys.argv[2:])


if __name__ == "__main__":
    main()
