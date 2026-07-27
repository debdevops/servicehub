"""Pydantic request/response models for the ServiceHub AI service.

FeatureRecord intentionally has no field for message body or payload content —
the .NET side extracts only structured features (sizes, hashes, categorical
labels) before sending anything here. See MessageFeatures.cs /
MessageFeatureRecord.cs on the .NET side for the source-of-truth shape.
"""

from pydantic import BaseModel


class FeatureRecord(BaseModel):
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


class AnalyzeResponse(BaseModel):
    clusters: list[dict] = []
    explanation: str | None = None


class HealthResponse(BaseModel):
    status: str
    version: str
    ready: bool
