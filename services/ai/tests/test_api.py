from fastapi.testclient import TestClient

from app.main import app

client = TestClient(app)

VALID_RECORD = {
    "delivery_count": 3,
    "body_size_bytes": 1024,
    "time_to_deadletter_seconds": 12.5,
    "seconds_since_enqueued": 45.0,
    "hour_of_day": 14,
    "day_of_week": 2,
    "property_count": 5,
    "provider": "AzureServiceBus",
    "entity_name": "orders-queue",
    "deadletter_reason": "MaxDeliveryCountExceeded",
    "exception_type": "System.TimeoutException",
    "content_type": "application/json",
    "payload_shape": "json_object",
    "error_text_normalised": "timeout waiting for downstream response",
    "schema_fingerprint": "a1b2c3d4",
    "feature_version": 1,
}


def test_health_returns_ready():
    response = client.get("/health")

    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "ok"
    assert body["ready"] is True
    assert "version" in body


def test_analyze_returns_stub_shape_for_records():
    response = client.post("/analyze", json={"records": [VALID_RECORD]})

    assert response.status_code == 200
    body = response.json()
    assert body == {"clusters": [], "explanation": None}


def test_analyze_returns_stub_shape_for_empty_records():
    response = client.post("/analyze", json={"records": []})

    assert response.status_code == 200
    assert response.json() == {"clusters": [], "explanation": None}


def test_analyze_rejects_missing_required_field():
    incomplete_record = {k: v for k, v in VALID_RECORD.items() if k != "provider"}

    response = client.post("/analyze", json={"records": [incomplete_record]})

    assert response.status_code == 422


def test_feature_record_has_no_body_field():
    from app.models import FeatureRecord

    field_names = set(FeatureRecord.model_fields.keys())
    forbidden = {"body", "message_body", "payload", "payload_body", "content"}
    assert field_names.isdisjoint(forbidden)
