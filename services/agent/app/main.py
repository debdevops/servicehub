"""FastAPI skeleton for the ServiceHub reasoning-companion service (roadmap §7, W5).

Disabled by default: OLLAMA_HOST unset means /propose always returns an empty
list with method="disabled" — the honest default posture is "no reasoning
companion", not a silently-degraded one. See app/reasoning.py for the
non-negotiable invariants this service never violates.
"""

import ipaddress
import logging
import os
import socket
from urllib.parse import urlparse

from fastapi import FastAPI

from app.models import HealthResponse, ProposeRequest, ProposeResponse
from app.reasoning import generate_proposals

logger = logging.getLogger("servicehub.agent.main")

VERSION = "0.1.0"


def _is_approved_local_endpoint(raw_host: str) -> bool:
    """True only if every address `raw_host` resolves to is loopback, private,
    or link-local — the self-hosted-only boundary reasoning.py promises (see
    its module docstring). Any public address in the resolution, or a DNS
    failure, rejects the whole host: an operator override of OLLAMA_HOST must
    not be able to silently redirect evidence to a remote/cloud endpoint.
    """
    parsed = urlparse(raw_host if "://" in raw_host else f"//{raw_host}")
    hostname = parsed.hostname
    if not hostname:
        return False

    try:
        addrinfo = socket.getaddrinfo(hostname, None)
    except socket.gaierror:
        return False

    if not addrinfo:
        return False

    for *_rest, sockaddr in addrinfo:
        try:
            ip = ipaddress.ip_address(sockaddr[0])
        except ValueError:
            return False
        if not (ip.is_loopback or ip.is_private or ip.is_link_local):
            return False

    return True


def _resolve_ollama_host() -> str | None:
    raw = os.environ.get("OLLAMA_HOST", "").strip()
    if not raw:
        return None
    if not _is_approved_local_endpoint(raw):
        logger.warning(
            "OLLAMA_HOST=%r does not resolve to an approved local/private "
            "endpoint; disabling the reasoning companion instead of "
            "contacting it.",
            raw,
        )
        return None
    return raw


OLLAMA_HOST = _resolve_ollama_host()
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
