#!/usr/bin/env python3
"""W1.3 soak-run harness: drives ServiceHub against real cloud dead-letter traffic
(via servicehub-samples) to observe an actual L3->L4 autonomy promotion and a real
unattended (autonomous) replay execution, then exports and independently verifies
the resulting evidence.

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
  trust <signature-hash>
  autonomy <signature-hash>
  ledger-entries <namespace-id> [limit]
  export <operation-id> <out-path.zip>
  consumer-pause <provider> <entity>
  consumer-resume <provider> <entity>
"""
import json
import sys
import time

import requests

SH_BASE = "http://localhost:5153"
SAMPLES_BASE = "http://localhost:5280"
API_KEY = "a1a1a1a1000000000000000000000000000000000000000000000000scopefull"
HEADERS = {"X-API-KEY": API_KEY, "Content-Type": "application/json"}


def sh(method, path, **kwargs):
    r = requests.request(method, f"{SH_BASE}{path}", headers=HEADERS, timeout=30, **kwargs)
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
    status, body = sh(
        "POST",
        f"/api/v1/namespaces/{namespace_id}/dlq/signatures/{signature_hash}/replay",
        json={},
    )
    out(status, body)


def cmd_trust(signature_hash):
    status, body = sh("GET", f"/api/v1/recovery/trust/{signature_hash}?actionKind=Replay")
    out(status, body)


def cmd_autonomy(signature_hash):
    status, body = sh("GET", f"/api/v1/recovery/autonomy/{signature_hash}?actionKind=Replay")
    out(status, body)


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
    "trust": cmd_trust,
    "autonomy": cmd_autonomy,
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
