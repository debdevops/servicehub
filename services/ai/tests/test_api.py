from fastapi.testclient import TestClient

from app.main import app

client = TestClient(app)

VALID_RECORD = {
    "ref": "msg-1",
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


def test_analyze_single_record_falls_back_to_grouped():
    response = client.post("/analyze", json={"records": [VALID_RECORD]})

    assert response.status_code == 200
    body = response.json()
    assert body["method"] == "grouped"
    assert body["singletons"] == []
    assert len(body["clusters"]) == 1
    assert body["clusters"][0]["size"] == 1
    assert body["explanation"] is None


def test_analyze_returns_empty_shape_for_empty_records():
    response = client.post("/analyze", json={"records": []})

    assert response.status_code == 200
    assert response.json() == {
        "clusters": [],
        "singletons": [],
        "method": "clustered",
        "explanation": None,
    }


def test_analyze_rejects_missing_required_field():
    incomplete_record = {k: v for k, v in VALID_RECORD.items() if k != "provider"}

    response = client.post("/analyze", json={"records": [incomplete_record]})

    assert response.status_code == 422


def test_feature_record_has_no_body_field():
    from app.models import FeatureRecord

    field_names = set(FeatureRecord.model_fields.keys())
    forbidden = {"body", "message_body", "payload", "payload_body", "content"}
    assert field_names.isdisjoint(forbidden)


def test_analyze_round_trips_ref_for_single_record():
    record = {**VALID_RECORD, "ref": "ref-a"}

    response = client.post("/analyze", json={"records": [record]})

    assert response.status_code == 200
    cluster = response.json()["clusters"][0]
    assert cluster["representative_ref"] == "ref-a"
    assert cluster["first_occurrence_ref"] == "ref-a"
    assert cluster["last_occurrence_ref"] == "ref-a"


def test_analyze_refs_correct_when_refs_do_not_match_position_order():
    # Regression test: refs are opaque strings unrelated to list position.
    # If the response ever leaked a raw positional index instead of a ref,
    # this would fail because none of these refs equal their position.
    records = [
        {**VALID_RECORD, "ref": "zzz-third"},
        {**VALID_RECORD, "ref": "aaa-first"},
        {**VALID_RECORD, "ref": "mmm-second"},
    ]

    response = client.post("/analyze", json={"records": records})

    assert response.status_code == 200
    cluster = response.json()["clusters"][0]
    assert cluster["first_occurrence_ref"] == "zzz-third"
    assert cluster["last_occurrence_ref"] == "mmm-second"
    assert cluster["representative_ref"] in {"zzz-third", "aaa-first", "mmm-second"}


def test_analyze_rejects_missing_ref():
    incomplete_record = {k: v for k, v in VALID_RECORD.items() if k != "ref"}

    response = client.post("/analyze", json={"records": [incomplete_record]})

    assert response.status_code == 422


def test_analyze_rejects_duplicate_refs():
    response = client.post("/analyze", json={"records": [VALID_RECORD, VALID_RECORD]})

    assert response.status_code == 422
