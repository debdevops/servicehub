from fastapi.testclient import TestClient

import app.main as main_module
from app.main import app

client = TestClient(app)

VALID_RECORD = {
    "ref": "ns-1:sig-1",
    "signature_hash": "sig-1",
    "lifecycle_status": "Active",
    "severity": "Warning",
    "provider": "AzureServiceBus",
    "dominant_deadletter_reason": "MaxDeliveryCountExceeded",
    "top_terms": ["timeout", "downstream"],
    "occurrence_count": 12,
    "blast_radius": 12,
    "is_recurring": False,
    "pending_decision_count": 1,
    "recovery_entry_count": 3,
    "open_recovery_entry_count": 0,
    "anomaly_flag_count": 1,
    "drift_finding_count": 0,
    "correlation_hypothesis_count": 0,
    "prevention_trigger_count": 0,
    "replay_plan_count": 0,
}


def test_health_returns_ready():
    response = client.get("/health")

    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "ok"
    assert body["ready"] is True
    assert "version" in body


def test_health_reports_reasoning_not_configured_by_default(monkeypatch):
    monkeypatch.setattr(main_module, "OLLAMA_HOST", None)

    response = client.get("/health")

    assert response.json()["reasoning_configured"] is False


def test_propose_returns_disabled_when_no_ollama_host_configured(monkeypatch):
    monkeypatch.setattr(main_module, "OLLAMA_HOST", None)

    response = client.post("/propose", json={"records": [VALID_RECORD]})

    assert response.status_code == 200
    body = response.json()
    assert body == {"proposals": [], "method": "disabled", "model": None}


def test_propose_returns_empty_shape_for_empty_records(monkeypatch):
    monkeypatch.setattr(main_module, "OLLAMA_HOST", None)

    response = client.post("/propose", json={"records": []})

    assert response.status_code == 200
    assert response.json() == {"proposals": [], "method": "disabled", "model": None}


def test_propose_rejects_missing_required_field():
    incomplete_record = {k: v for k, v in VALID_RECORD.items() if k != "signature_hash"}

    response = client.post("/propose", json={"records": [incomplete_record]})

    assert response.status_code == 422


def test_propose_rejects_duplicate_refs():
    response = client.post("/propose", json={"records": [VALID_RECORD, VALID_RECORD]})

    assert response.status_code == 422


def test_resolve_ollama_host_rejects_public_endpoint():
    assert main_module._resolve_approved_endpoint("example.com") is None


def test_resolve_ollama_host_accepts_loopback():
    assert (
        main_module._resolve_approved_endpoint("http://127.0.0.1:11434")
        == "http://127.0.0.1:11434"
    )


def test_resolve_ollama_host_accepts_private_ip():
    assert (
        main_module._resolve_approved_endpoint("http://192.168.1.50:11434")
        == "http://192.168.1.50:11434"
    )


def test_resolve_ollama_host_rejects_public_ip():
    assert main_module._resolve_approved_endpoint("http://8.8.8.8:11434") is None


def test_resolve_ollama_host_pins_literal_ip_for_hostname():
    # The literal IP the DNS name resolved to during approval must be what
    # the outbound request actually connects to — never the hostname again —
    # or a rebound name could point at a public host by request time.
    pinned = main_module._resolve_approved_endpoint("http://localhost:11434")
    assert pinned in ("http://127.0.0.1:11434", "http://[::1]:11434")


def test_resolve_ollama_host_rejects_cloud_metadata_endpoint():
    assert main_module._resolve_approved_endpoint("http://169.254.169.254/") is None


def test_resolve_ollama_host_disables_on_unapproved_override(monkeypatch):
    monkeypatch.setenv("OLLAMA_HOST", "http://8.8.8.8:11434")

    assert main_module._resolve_ollama_host() is None


def test_evidence_record_has_no_body_field():
    from app.models import EvidenceRecord

    field_names = set(EvidenceRecord.model_fields.keys())
    forbidden = {"body", "message_body", "payload", "payload_body", "content", "message_text"}
    assert field_names.isdisjoint(forbidden)


def test_proposal_has_no_confidence_field():
    from app.models import Proposal

    field_names = set(Proposal.model_fields.keys())
    forbidden = {"confidence", "confidence_score", "probability", "score"}
    assert field_names.isdisjoint(forbidden)
