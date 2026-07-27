import re

from app.clustering import analyze_records
from app.models import FeatureRecord

BASE_KWARGS = {
    "delivery_count": 3,
    "body_size_bytes": 1024,
    "time_to_deadletter_seconds": 12.5,
    "seconds_since_enqueued": 45.0,
    "hour_of_day": 14,
    "day_of_week": 2,
    "property_count": 5,
    "provider": "AzureServiceBus",
    "content_type": "application/json",
    "payload_shape": "json_object",
    "schema_fingerprint": "a1b2c3d4",
    "feature_version": 1,
}


def make_record(error_text_normalised, entity_name="orders-queue",
                 deadletter_reason="MaxDeliveryCountExceeded",
                 exception_type="System.TimeoutException") -> FeatureRecord:
    return FeatureRecord(
        **BASE_KWARGS,
        entity_name=entity_name,
        deadletter_reason=deadletter_reason,
        exception_type=exception_type,
        error_text_normalised=error_text_normalised,
    )


_GUID_PATTERN = re.compile(
    r"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", re.IGNORECASE
)
_TIMESTAMP_PATTERN = re.compile(
    r"\b\d{4}-\d{2}-\d{2}[t ]\d{2}:\d{2}:\d{2}(\.\d+)?(z)?\b", re.IGNORECASE
)


def normalise(reason: str, description: str) -> str:
    """Minimal stand-in for SignalExtractor.NormaliseErrorText, covering just
    the guid/timestamp substitutions this test needs."""
    text = f"{reason} {description}".strip().lower()
    text = _GUID_PATTERN.sub("<guid>", text)
    text = _TIMESTAMP_PATTERN.sub("<timestamp>", text)
    return text


FAILURE_MODES = [
    (
        "MaxDeliveryCountExceeded",
        "System.TimeoutException",
        "orders-queue",
        "system.timeoutexception timeout waiting for downstream response from "
        "payment-gateway after 5000 ms endpoint <url>",
    ),
    (
        "MaxDeliveryCountExceeded",
        "System.IO.IOException",
        "shipping-queue",
        "system.io.ioexception failed to write manifest blob to storage "
        "container <guid> disk quota exceeded retrying",
    ),
    (
        "UserAbandoned",
        "Newtonsoft.Json.JsonSerializationException",
        "orders-queue",
        "newtonsoft.json.jsonserializationexception unable to deserialize "
        "payload schema mismatch expected field <guid> not found in body",
    ),
]


def test_three_known_failure_modes_cluster_into_three_groups():
    records = []
    for reason, exc, entity, text in FAILURE_MODES:
        for _ in range(34):
            records.append(make_record(text, entity_name=entity, deadletter_reason=reason,
                                        exception_type=exc))

    result = analyze_records(records)

    assert result["method"] == "clustered"
    assert len(result["clusters"]) == 3
    sizes = sorted(c["size"] for c in result["clusters"])
    assert sizes == [34, 34, 34]


def test_identical_errors_differing_only_by_guid_or_timestamp_cluster_together():
    variants = [
        normalise("MaxDeliveryCountExceeded",
                  "System.TimeoutException call to order-svc failed at 2024-03-11t10:15:30z "
                  "transaction 5f8c1a2b-3d4e-4f5a-9b6c-7d8e9f0a1b2c"),
        normalise("MaxDeliveryCountExceeded",
                  "System.TimeoutException call to order-svc failed at 2025-07-02t22:41:09z "
                  "transaction a1b2c3d4-e5f6-4789-8abc-def012345678"),
        normalise("MaxDeliveryCountExceeded",
                  "System.TimeoutException call to order-svc failed at 2023-01-05t03:07:55z "
                  "transaction 00000000-0000-4000-8000-000000000000"),
    ]
    assert len(set(variants)) == 1, "normalisation should collapse guid/timestamp noise"

    # Repeat each variant enough times to clear DBSCAN's min_samples threshold,
    # plus enough unrelated noise that the batch isn't trivially one cluster.
    records = [make_record(text) for text in variants for _ in range(10)]
    records += [
        make_record("system.nullreferenceexception object reference not set at handler.run")
        for _ in range(10)
    ]

    result = analyze_records(records)

    timeout_cluster = next(
        c for c in result["clusters"] if c["dominant_deadletter_reason"] == "MaxDeliveryCountExceeded"
    )
    assert timeout_cluster["size"] == 30


def test_genuinely_unrelated_errors_do_not_cluster_together():
    unrelated_texts = [
        "system.formatexception input string was not in a correct format for field quantity",
        "grpc.core.rpcexception status deadline exceeded calling warehouse allocator",
        "system.security.cryptography.cryptographicexception padding is invalid",
        "system.xml.xmlexception root element is missing in document",
        "custom.validation.skurangeexception sku code outside of allowed numeric range",
    ]
    # Give DBSCAN something dense to work with too, so a real cluster exists
    # alongside the unrelated singles rather than everything being noise.
    dense_text = FAILURE_MODES[0][3]
    records = [make_record(dense_text) for _ in range(20)]
    records += [make_record(text) for text in unrelated_texts]

    result = analyze_records(records)

    assert result["method"] == "clustered"
    dense_cluster = next(c for c in result["clusters"] if c["size"] == 20)
    unrelated_singleton_indices = {i for i, s in enumerate(result["singletons"])}
    assert len(unrelated_singleton_indices) == len(unrelated_texts)
    assert dense_cluster["size"] == 20


def test_empty_input_handled():
    result = analyze_records([])

    assert result == {"clusters": [], "singletons": [], "method": "clustered"}


def test_single_message_handled():
    records = [make_record(FAILURE_MODES[0][3])]

    result = analyze_records(records)

    assert result["method"] == "grouped"
    assert result["singletons"] == []
    assert len(result["clusters"]) == 1
    assert result["clusters"][0]["size"] == 1
    assert result["clusters"][0]["representative_index"] == 0


_PATHOLOGICAL_TEXTS = [
    "system.formatexception input string was not in a correct format for field quantity",
    "grpc.core.rpcexception status deadline exceeded calling warehouse allocator",
    "system.security.cryptography.cryptographicexception padding is invalid",
    "system.xml.xmlexception root element is missing in document",
    "custom.validation.skurangeexception sku code outside of allowed numeric range",
    "system.stackoverflowexception recursive validation loop detected in ruleengine",
    "system.unauthorizedaccessexception access to blob path is denied entirely",
    "system.notsupportedexception the given content type is not supported here",
    "system.outofmemoryexception exception of type outofmemoryexception was thrown",
    "system.invalidoperationexception sequence contains no matching element found",
    "system.threading.tasks.taskcanceledexception a background task was canceled",
    "system.net.sockets.socketexception no route to host on that subnet",
    "system.componentmodel.datacaptureexception unexpected control character seen",
    "system.text.decoderfallbackexception unable to translate byte at that index",
    "argumentoutofrangeexception index was out of range must be non-negative here",
    "system.io.pathtoolongexception the specified path exceeds the maximum length",
    "system.arithmeticoverflowexception overflow occurred during arithmetic operation",
    "system.membershipcreateuserexception the username is already in use elsewhere",
    "system.data.constraintexception a constraint could not be enforced on write",
    "system.reflection.targetinvocationexception exception has been thrown by target",
    "system.uri.urifformatexception invalid uri the format could not be determined",
    "system.web.httpparseexception could not parse malformed markup in template",
    "system.serviceprocess.timeoutexception the service did not respond in time",
    "system.configuration.settingsproperynotfoundexception no such setting exists",
    "system.diagnostics.eventing.eventsourceexception failed to write trace event",
    "system.resources.missingmanifestresourceexception no resource file found",
    "system.runtime.interopservices.seh_exception native call failed unexpectedly",
    "system.globalization.cultureexception unsupported culture identifier passed",
    "system.speech.recognition.recognizeraudioexception audio device unavailable",
    "system.printing.printqueueexception the print queue could not be reached",
]


def test_fallback_triggers_on_pathological_input():
    # 30 genuinely distinct error texts, no two sharing enough vocabulary to
    # meet DBSCAN's min_samples=2 anywhere -> 0% coverage < 50% threshold ->
    # fallback to (deadletter_reason, exception_type) grouping.
    records = [make_record(text) for text in _PATHOLOGICAL_TEXTS]

    result = analyze_records(records)

    assert result["method"] == "grouped"
    assert result["singletons"] == []
    assert sum(c["size"] for c in result["clusters"]) == 30
