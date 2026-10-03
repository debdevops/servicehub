"""Pydantic request/response models for the ServiceHub reasoning-companion service.

EvidenceRecord intentionally carries no message-body or payload field, the same
discipline services/ai/app/models.py documents for FeatureRecord: the .NET side
sends only structured, already-aggregated incident evidence (counts, lifecycle
status, normalised terms) — never a message's own content. See
ReasoningEvidenceContext.cs / ReasoningEvidenceMapper.cs on the .NET side for
the source-of-truth shape this mirrors.
"""

from pydantic import BaseModel, Field, model_validator


class EvidenceRecord(BaseModel):
    # Caller-supplied opaque identity, round-tripped in each proposal instead
    # of a positional index — same reasoning as FeatureRecord.ref in services/ai.
    ref: str = Field(min_length=1)

    signature_hash: str
    lifecycle_status: str
    severity: str
    provider: str | None = None
    dominant_deadletter_reason: str | None = None
    top_terms: list[str] = []

    occurrence_count: int
    blast_radius: int
    is_recurring: bool
    pending_decision_count: int
    recovery_entry_count: int
    open_recovery_entry_count: int
    anomaly_flag_count: int
    drift_finding_count: int
    correlation_hypothesis_count: int
    prevention_trigger_count: int
    replay_plan_count: int


class ProposeRequest(BaseModel):
    records: list[EvidenceRecord]

    @model_validator(mode="after")
    def _refs_must_be_unique(self) -> "ProposeRequest":
        refs = [r.ref for r in self.records]
        if len(refs) != len(set(refs)):
            raise ValueError("records[].ref must be unique within a request")
        return self


class Proposal(BaseModel):
    # Which evidence record this proposal is about — the .NET side resolves
    # this back to (OwnerId, NamespaceId, SignatureHash), never trusted as an
    # identity on its own.
    ref: str

    # A short, human-readable statement of what was observed and why it might
    # matter — this is advisory text for a human reviewer, never a directive
    # and never a confidence score. The .NET side records it verbatim in the
    # Playbook Ledger's ProposalJson; nothing reads it back as a number to
    # gate autonomy.
    summary: str
    considerations: list[str] = []


class ProposeResponse(BaseModel):
    proposals: list[Proposal] = []

    # "ollama" when a local model actually produced proposals, "disabled"
    # when no local model is configured (the default posture), "unavailable"
    # when one is configured but unreachable or returned something this
    # service could not parse. Never raises a 5xx for either degraded case —
    # mirrors services/ai's "every failure path degrades the same way".
    method: str = "disabled"
    model: str | None = None


class HealthResponse(BaseModel):
    status: str
    version: str
    ready: bool
    # Whether a local reasoning backend is configured at all — distinct from
    # "ready", which only means the HTTP server itself is up. A caller uses
    # this to decide whether calling /propose is worth attempting.
    reasoning_configured: bool
