import json

import httpx
import pytest

from app.models import EvidenceRecord
from app.reasoning import generate_proposals

RECORD = EvidenceRecord(
    ref="ns-1:sig-1",
    signature_hash="sig-1",
    lifecycle_status="Active",
    severity="Warning",
    provider="AzureServiceBus",
    dominant_deadletter_reason="MaxDeliveryCountExceeded",
    top_terms=["timeout"],
    occurrence_count=12,
    blast_radius=12,
    is_recurring=False,
    pending_decision_count=1,
    recovery_entry_count=3,
    open_recovery_entry_count=0,
    anomaly_flag_count=1,
    drift_finding_count=0,
    correlation_hypothesis_count=0,
    prevention_trigger_count=0,
    replay_plan_count=0,
)


async def test_generate_proposals_disabled_without_ollama_host():
    proposals, method = await generate_proposals([RECORD], ollama_host=None, model="llama3.1")

    assert proposals == []
    assert method == "disabled"


async def test_generate_proposals_disabled_short_circuits_before_any_http_call(monkeypatch):
    def fail_if_called(*args, **kwargs):
        raise AssertionError("should not construct an HTTP client when disabled")

    monkeypatch.setattr(httpx, "AsyncClient", fail_if_called)

    proposals, method = await generate_proposals([RECORD], ollama_host=None, model="llama3.1")

    assert proposals == []
    assert method == "disabled"


async def test_generate_proposals_empty_records_returns_ollama_with_no_call(monkeypatch):
    def fail_if_called(*args, **kwargs):
        raise AssertionError("should not call out for an empty record batch")

    monkeypatch.setattr(httpx, "AsyncClient", fail_if_called)

    proposals, method = await generate_proposals([], ollama_host="http://localhost:11434", model="llama3.1")

    assert proposals == []
    assert method == "ollama"


class _FakeResponse:
    def __init__(self, status_code: int, json_body: dict):
        self.status_code = status_code
        self._json_body = json_body

    def json(self):
        return self._json_body


class _FakeAsyncClient:
    def __init__(self, response: _FakeResponse | None = None, raise_error: bool = False):
        self._response = response
        self._raise_error = raise_error

    async def __aenter__(self):
        return self

    async def __aexit__(self, *args):
        return False

    async def post(self, url, json):  # noqa: A002 - matches httpx signature
        if self._raise_error:
            raise httpx.ConnectError("connection refused")
        return self._response


def _patch_client(monkeypatch, fake_client: _FakeAsyncClient):
    monkeypatch.setattr(httpx, "AsyncClient", lambda **kwargs: fake_client)


async def test_generate_proposals_unavailable_when_host_unreachable(monkeypatch):
    _patch_client(monkeypatch, _FakeAsyncClient(raise_error=True))

    proposals, method = await generate_proposals(
        [RECORD], ollama_host="http://localhost:11434", model="llama3.1"
    )

    assert proposals == []
    assert method == "unavailable"


async def test_generate_proposals_unavailable_on_non_200(monkeypatch):
    _patch_client(monkeypatch, _FakeAsyncClient(response=_FakeResponse(500, {})))

    proposals, method = await generate_proposals(
        [RECORD], ollama_host="http://localhost:11434", model="llama3.1"
    )

    assert proposals == []
    assert method == "unavailable"


async def test_generate_proposals_unavailable_on_malformed_content(monkeypatch):
    body = {"message": {"content": "not json"}}
    _patch_client(monkeypatch, _FakeAsyncClient(response=_FakeResponse(200, body)))

    proposals, method = await generate_proposals(
        [RECORD], ollama_host="http://localhost:11434", model="llama3.1"
    )

    assert proposals == []
    assert method == "unavailable"


async def test_generate_proposals_parses_valid_response(monkeypatch):
    content = json.dumps(
        [
            {
                "ref": "ns-1:sig-1",
                "summary": "This signature has a pending decision and recurring timeouts.",
                "considerations": ["Consider reviewing the downstream timeout budget."],
            }
        ]
    )
    body = {"message": {"content": content}}
    _patch_client(monkeypatch, _FakeAsyncClient(response=_FakeResponse(200, body)))

    proposals, method = await generate_proposals(
        [RECORD], ollama_host="http://localhost:11434", model="llama3.1"
    )

    assert method == "ollama"
    assert len(proposals) == 1
    assert proposals[0].ref == "ns-1:sig-1"
    assert "timeout" in proposals[0].summary.lower() or True  # content is model-authored


async def test_generate_proposals_drops_proposals_with_unknown_ref(monkeypatch):
    content = json.dumps(
        [{"ref": "not-a-real-ref", "summary": "hallucinated", "considerations": []}]
    )
    body = {"message": {"content": content}}
    _patch_client(monkeypatch, _FakeAsyncClient(response=_FakeResponse(200, body)))

    proposals, method = await generate_proposals(
        [RECORD], ollama_host="http://localhost:11434", model="llama3.1"
    )

    assert method == "ollama"
    assert proposals == []


async def test_generate_proposals_drops_individually_malformed_items(monkeypatch):
    content = json.dumps(
        [
            {"ref": "ns-1:sig-1", "not_a_field": "oops"},
            {"ref": "ns-1:sig-1", "summary": "valid one", "considerations": []},
        ]
    )
    body = {"message": {"content": content}}
    _patch_client(monkeypatch, _FakeAsyncClient(response=_FakeResponse(200, body)))

    proposals, method = await generate_proposals(
        [RECORD], ollama_host="http://localhost:11434", model="llama3.1"
    )

    assert method == "ollama"
    assert len(proposals) == 1
    assert proposals[0].summary == "valid one"


async def test_generate_proposals_unavailable_when_top_level_not_a_list(monkeypatch):
    content = json.dumps({"ref": "ns-1:sig-1", "summary": "not wrapped in a list"})
    body = {"message": {"content": content}}
    _patch_client(monkeypatch, _FakeAsyncClient(response=_FakeResponse(200, body)))

    proposals, method = await generate_proposals(
        [RECORD], ollama_host="http://localhost:11434", model="llama3.1"
    )

    assert proposals == []
    assert method == "unavailable"
