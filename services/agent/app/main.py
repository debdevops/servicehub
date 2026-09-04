"""FastAPI skeleton for the ServiceHub reasoning-companion service (roadmap §7, W5).

Disabled by default: OLLAMA_HOST unset means /propose always returns an empty
list with method="disabled" — the honest default posture is "no reasoning
companion", not a silently-degraded one. See app/reasoning.py for the
non-negotiable invariants this service never violates.
"""

import os

from fastapi import FastAPI

from app.models import HealthResponse, ProposeRequest, ProposeResponse
from app.reasoning import generate_proposals

VERSION = "0.1.0"

OLLAMA_HOST = os.environ.get("OLLAMA_HOST", "").strip() or None
OLLAMA_MODEL = os.environ.get("OLLAMA_MODEL", "").strip() or "llama3.1"

app = FastAPI(title="ServiceHub Reasoning Companion", version=VERSION)


@app.get("/health", response_model=HealthResponse)
def health() -> HealthResponse:
    return HealthResponse(
        status="ok",
        version=VERSION,
        ready=True,
        reasoning_configured=OLLAMA_HOST is not None,
    )


@app.post("/propose", response_model=ProposeResponse)
async def propose(request: ProposeRequest) -> ProposeResponse:
    proposals, method = await generate_proposals(request.records, OLLAMA_HOST, OLLAMA_MODEL)
    return ProposeResponse(
        proposals=proposals,
        method=method,
        model=OLLAMA_MODEL if method == "ollama" else None,
    )
