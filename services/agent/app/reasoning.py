"""Local-LLM reasoning over structured evidence, via a self-hosted Ollama instance.

Non-negotiable invariants (roadmap §7 — this file must never violate these,
not "for now", permanently):
  - This service never calls an external/cloud LLM API. OLLAMA_HOST is the
    only backend it knows how to talk to, and it is expected to be a
    same-network or same-host container the operator runs themselves.
  - A proposal is advisory text only. Nothing this module produces is, or
    contains, a confidence score, an approval, or an instruction to execute
    anything — the ledger entries it feeds into can only ever reach
    "Approved" via a human's own DispositionAsync call on the .NET side.
  - Every failure path (unreachable host, timeout, malformed JSON, model
    refusing to answer) degrades to an empty proposal list with method
    "unavailable" — this function must never raise into its caller.
"""

import json
import logging

import httpx

from app.models import EvidenceRecord, Proposal

logger = logging.getLogger("servicehub.agent.reasoning")

REQUEST_TIMEOUT_SECONDS = 20.0

SYSTEM_PROMPT = (
    "You are an advisory-only reasoning layer inside ServiceHub, a dead-letter-queue "
    "operations tool. You are given structured, already-aggregated evidence about a "
    "failure signature — counts, lifecycle status, normalised error terms — never a "
    "message's raw content. You do not execute, approve, or promote anything, and you "
    "have no access to do so. Your only output is a short, plain-language observation "
    "a human operator might find useful when they review this signature. Never invent "
    "a confidence score or numeric probability. Never instruct the reader to take a "
    "specific automated action. If the evidence does not suggest anything noteworthy, "
    "omit that record from your response entirely rather than inventing an observation.\n\n"
    "Respond with a JSON array only, no prose outside it. Each element must have the "
    "shape: {\"ref\": <the record's ref, verbatim>, \"summary\": <one or two sentences>, "
    "\"considerations\": [<zero or more short strings>]}. Omit any record you have "
    "nothing useful to say about."
)


def _record_prompt(record: EvidenceRecord) -> str:
    # model_dump_json is already a compact, structured, payload-free
    # representation — no separate hand-written template needed.
    return record.model_dump_json()


async def generate_proposals(
    records: list[EvidenceRecord],
    ollama_host: str | None,
    model: str,
) -> tuple[list[Proposal], str]:
    """Returns (proposals, method). method is "disabled" | "ollama" | "unavailable"."""

    if not ollama_host:
        return [], "disabled"

    if not records:
        return [], "ollama"

    user_prompt = "Evidence records:\n" + "\n".join(_record_prompt(r) for r in records)

    payload = {
        "model": model,
        "messages": [
            {"role": "system", "content": SYSTEM_PROMPT},
            {"role": "user", "content": user_prompt},
        ],
        "format": "json",
        "stream": False,
    }

    try:
        async with httpx.AsyncClient(timeout=REQUEST_TIMEOUT_SECONDS) as client:
            response = await client.post(f"{ollama_host.rstrip('/')}/api/chat", json=payload)
    except httpx.HTTPError:
        logger.warning("Ollama host %s unreachable or timed out", ollama_host)
        return [], "unavailable"

    if response.status_code != 200:
        logger.warning("Ollama returned HTTP %s", response.status_code)
        return [], "unavailable"

    try:
        body = response.json()
        content = body["message"]["content"]
        raw_proposals = json.loads(content)
    except (json.JSONDecodeError, KeyError, TypeError):
        logger.warning("Could not parse Ollama response as the expected JSON shape")
        return [], "unavailable"

    if not isinstance(raw_proposals, list):
        return [], "unavailable"

    valid_refs = {r.ref for r in records}
    proposals: list[Proposal] = []
    for item in raw_proposals:
        try:
            proposal = Proposal.model_validate(item)
        except Exception:  # noqa: BLE001 — one malformed item must not drop the rest
            continue
        if proposal.ref not in valid_refs:
            continue
        proposals.append(proposal)

    return proposals, "ollama"
