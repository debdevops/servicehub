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


# Cloud instance-metadata endpoints sit inside the link-local range this
# service otherwise approves. An OLLAMA_HOST override must never be able to
# point the agent at one of these, so they're excluded explicitly rather
# than relying on the link-local check alone.
_BLOCKED_ADDRESSES = frozenset(
    ipaddress.ip_address(addr)
    for addr in (
        "169.254.169.254",  # AWS / GCP / Azure / DigitalOcean / Oracle IMDS
        "fd00:ec2::254",  # AWS IMDSv2 IPv6
    )
)


def _is_approved_address(ip: ipaddress.IPv4Address | ipaddress.IPv6Address) -> bool:
    if ip in _BLOCKED_ADDRESSES:
        return False
    return bool(ip.is_loopback or ip.is_private or ip.is_link_local)


def _resolve_approved_endpoint(raw_host: str) -> str | None:
    """Resolves `raw_host` and, if every address it resolves to is approved
    (see `_is_approved_address`), returns a URL with the hostname replaced
    by a single pinned literal IP address. Returns None if resolution fails
    or any resolved address is not approved.

    The literal-IP pin matters as much as the approval check: this
    function's result is what the outbound HTTP request actually connects
    to, so a hostname that resolved to a private address here can never
    later re-resolve (DNS rebinding) to a public one at request time.
    """
    parsed = urlparse(raw_host if "://" in raw_host else f"//{raw_host}")
    hostname = parsed.hostname
    if not hostname:
        return None

    try:
        addrinfo = socket.getaddrinfo(hostname, None)
    except socket.gaierror:
        return None

    if not addrinfo:
        return None

    pinned_ip: str | None = None
    for *_rest, sockaddr in addrinfo:
        try:
            ip = ipaddress.ip_address(sockaddr[0])
        except ValueError:
            return None
        if not _is_approved_address(ip):
            return None
        if pinned_ip is None:
            pinned_ip = sockaddr[0]

    if pinned_ip is None:
        return None

    netloc = f"[{pinned_ip}]" if ":" in pinned_ip else pinned_ip
    if parsed.port:
        netloc = f"{netloc}:{parsed.port}"

    return f"{parsed.scheme or 'http'}://{netloc}{parsed.path}"


def _resolve_ollama_host() -> str | None:
    raw = os.environ.get("OLLAMA_HOST", "").strip()
    if not raw:
        return None
    pinned = _resolve_approved_endpoint(raw)
    if pinned is None:
        logger.warning(
            "OLLAMA_HOST=%r does not resolve to an approved local/private "
            "endpoint; disabling the reasoning companion instead of "
            "contacting it.",
            raw,
        )
        return None
    return pinned


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
