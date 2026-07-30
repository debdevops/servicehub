"""Pydantic request/response models for the ServiceHub AI service.

FeatureRecord intentionally has no field for message body or payload content —
the .NET side extracts only structured features (sizes, hashes, categorical
labels) before sending anything here. See MessageFeatures.cs /
MessageFeatureRecord.cs on the .NET side for the source-of-truth shape.
"""

from pydantic import BaseModel, Field, model_validator


class FeatureRecord(BaseModel):
    # Caller-supplied opaque identity, round-tripped in the response in place
    # of a positional index. This service never interprets it.
    ref: str = Field(min_length=1)

    # Numeric
    delivery_count: int
    body_size_bytes: int
    time_to_deadletter_seconds: float
    seconds_since_enqueued: float
    hour_of_day: int
    day_of_week: int
    property_count: int

    # Categorical
    provider: str
    entity_name: str
    deadletter_reason: str
    exception_type: str
    content_type: str
    payload_shape: str

    # Derived
    error_text_normalised: str
    schema_fingerprint: str
    feature_version: int


class AnalyzeRequest(BaseModel):
    records: list[FeatureRecord]

    @model_validator(mode="after")
    def _refs_must_be_unique(self) -> "AnalyzeRequest":
        refs = [r.ref for r in self.records]
        if len(refs) != len(set(refs)):
            raise ValueError("records[].ref must be unique within a request")
        return self


class AnalyzeResponse(BaseModel):
    # clusters/singletons entries carry *_ref fields (caller-supplied, opaque)
    # rather than positional indices into the request's records list. Indices
    # broke silently whenever the caller filtered, deduplicated, or reordered
    # records before or after calling this service — refs don't, because they
    # travel with the record itself. Do not reintroduce positional fields here.
    #
    # Each clusters[] entry additionally carries:
    #   - member_refs: the ref of every member of the cluster, in input order —
    #     lets a caller drill down from a cluster to the messages it represents.
    #   - dominant_deadletter_reason_count: how many members share
    #     dominant_deadletter_reason; less than size means the cluster's
    #     failure reasons are mixed.
    clusters: list[dict] = []
    singletons: list[dict] = []
    method: str = "clustered"
    explanation: str | None = None


class HealthResponse(BaseModel):
    status: str
    version: str
    ready: bool
