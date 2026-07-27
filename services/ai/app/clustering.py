"""Error-signature clustering for a batch of DLQ FeatureRecords.

Groups messages by error signature so an operator can tell whether a DLQ full
of messages is one root cause or several, using only TF-IDF + DBSCAN over
error_text_normalised — no embeddings, no training data, no cold start.

Positions in the caller's `records` list stand in for message identity and
occurrence order: FeatureRecord (the P0-4 contract) carries neither a message
id nor an absolute timestamp, only derived fields like hour_of_day. The caller
already knows which real message sits at each list position, so
representative_index/first_occurrence_index/last_occurrence_index are indices
into that list, not wall-clock timestamps.
"""

from collections import Counter

import numpy as np
from sklearn.cluster import DBSCAN
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.metrics.pairwise import cosine_distances

from app.models import FeatureRecord

# Grid-searched against a synthetic validation corpus (~1,100 messages: 6
# dominant failure templates + a diverse long tail of one-off errors) over
# eps in [0.15, 0.60]: the result was flat at 6 clusters / 95.8% coverage
# across the entire range on that corpus, so 0.35 (the midpoint) was picked
# rather than an edge value. min_samples=2 on char_wb(3,5) TF-IDF + cosine
# distance reliably separates dominant failure modes into their own clusters
# while still merging near-duplicate error text (same message differing only
# in the substituted <guid>/<num>/<timestamp> placeholders). Not exposed as
# an API input — nobody will tune this correctly by hand, and exposing it
# just invites bug reports.
DBSCAN_EPS = 0.35
DBSCAN_MIN_SAMPLES = 2

# Fallback thresholds: an operator-hostile clustering (too fragmented, or too
# much of the batch left unclustered) is worse than an honest simple grouping.
MAX_CLUSTERS_BEFORE_FALLBACK = 20
MIN_COVERAGE_BEFORE_FALLBACK = 0.5

TOP_TERMS_PER_CLUSTER = 6


def analyze_records(records: list[FeatureRecord]) -> dict:
    """Cluster records by error signature. Returns a dict matching AnalyzeResponse's
    clusters/singletons/method fields."""
    n = len(records)
    if n == 0:
        return {"clusters": [], "singletons": [], "method": "clustered"}

    texts = [r.error_text_normalised for r in records]

    labels = _dbscan_labels(texts)
    clustered_count = int(np.sum(labels >= 0))
    num_clusters = len(set(labels[labels >= 0]))
    coverage = clustered_count / n

    if num_clusters > MAX_CLUSTERS_BEFORE_FALLBACK or coverage < MIN_COVERAGE_BEFORE_FALLBACK:
        return _grouped_fallback(records, texts)

    return _clustered_result(records, texts, labels)


def _dbscan_labels(texts: list[str]) -> np.ndarray:
    char_vectorizer = TfidfVectorizer(analyzer="char_wb", ngram_range=(3, 5))
    char_matrix = char_vectorizer.fit_transform(texts)

    dbscan = DBSCAN(eps=DBSCAN_EPS, min_samples=DBSCAN_MIN_SAMPLES, metric="cosine")
    return dbscan.fit_predict(char_matrix)


def _clustered_result(records: list[FeatureRecord], texts: list[str], labels: np.ndarray) -> dict:
    top_terms_by_index = _fit_top_terms_lookup(texts)

    clusters = []
    for label in sorted(set(labels[labels >= 0])):
        member_indices = [i for i, lbl in enumerate(labels) if lbl == label]
        clusters.append(_build_summary(records, texts, member_indices, top_terms_by_index))

    clusters.sort(key=lambda c: c["size"], reverse=True)
    singletons = [_singleton_entry(i, records[i]) for i, lbl in enumerate(labels) if lbl < 0]

    return {"clusters": clusters, "singletons": singletons, "method": "clustered"}


def _grouped_fallback(records: list[FeatureRecord], texts: list[str]) -> dict:
    top_terms_by_index = _fit_top_terms_lookup(texts)

    groups: dict[tuple[str, str], list[int]] = {}
    for i, r in enumerate(records):
        key = (r.deadletter_reason, r.exception_type)
        groups.setdefault(key, []).append(i)

    clusters = [
        _build_summary(records, texts, member_indices, top_terms_by_index)
        for member_indices in groups.values()
    ]
    clusters.sort(key=lambda c: c["size"], reverse=True)

    return {"clusters": clusters, "singletons": [], "method": "grouped"}


def _fit_top_terms_lookup(texts: list[str]):
    """Returns a callable(member_indices) -> list[str] of the top distinguishing
    words/bigrams for that subset of texts. Separate from the char n-gram
    vectorizer used for distance: char n-grams aren't human-readable, so a
    word-level vectorizer is fit purely for reporting."""
    try:
        word_vectorizer = TfidfVectorizer(analyzer="word", ngram_range=(1, 2), stop_words="english")
        word_matrix = word_vectorizer.fit_transform(texts)
        feature_names = word_vectorizer.get_feature_names_out()
    except ValueError:
        # Empty vocabulary (e.g. all-empty/all-stopword text) — no terms to report.
        return lambda member_indices: []

    def lookup(member_indices: list[int]) -> list[str]:
        submatrix = word_matrix[member_indices]
        mean_scores = np.asarray(submatrix.mean(axis=0)).ravel()
        top_indices = np.argsort(mean_scores)[::-1]
        terms = []
        for idx in top_indices:
            if mean_scores[idx] <= 0 or len(terms) >= TOP_TERMS_PER_CLUSTER:
                break
            terms.append(feature_names[idx])
        return terms

    return lookup


def _build_summary(
    records: list[FeatureRecord],
    texts: list[str],
    member_indices: list[int],
    top_terms_lookup,
) -> dict:
    representative_index = _find_medoid(texts, member_indices)
    entities = Counter(records[i].entity_name for i in member_indices)
    reasons = Counter(records[i].deadletter_reason for i in member_indices)

    return {
        "size": len(member_indices),
        "representative_index": representative_index,
        "top_terms": top_terms_lookup(member_indices),
        "first_occurrence_index": min(member_indices),
        "last_occurrence_index": max(member_indices),
        "dominant_entity": entities.most_common(1)[0][0],
        "dominant_deadletter_reason": reasons.most_common(1)[0][0],
    }


def _find_medoid(texts: list[str], member_indices: list[int]) -> int:
    if len(member_indices) == 1:
        return member_indices[0]

    vectorizer = TfidfVectorizer(analyzer="char_wb", ngram_range=(3, 5))
    matrix = vectorizer.fit_transform([texts[i] for i in member_indices])
    distances = cosine_distances(matrix)
    total_distance = distances.sum(axis=1)
    medoid_position = int(np.argmin(total_distance))
    return member_indices[medoid_position]


def _singleton_entry(index: int, record: FeatureRecord) -> dict:
    return {
        "index": index,
        "dominant_entity": record.entity_name,
        "dominant_deadletter_reason": record.deadletter_reason,
    }
