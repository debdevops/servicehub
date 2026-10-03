# syntax=docker/dockerfile:1
# ServiceHub AI service — skeleton only, no algorithm yet (lands in P0-5).
#
# Internal Docker network only: this image must never be published to the host.
# It has no authentication of its own. See docker-compose.yml — no `ports:` entry.

FROM python:3.12-slim@sha256:57cd7c3a7a273101a6485ba99423ee568157882804b1124b4dd04266317710de

WORKDIR /app

# Install dependencies first for better layer caching.
COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

COPY app/ ./app/

RUN groupadd --system aiuser && useradd --system --gid aiuser --no-create-home aiuser
USER aiuser

EXPOSE 8000

# No curl in this slim image — reuse the interpreter already on PATH to hit the
# health endpoint instead of adding an extra package for a single check.
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD python -c "import urllib.request; urllib.request.urlopen('http://localhost:8000/health', timeout=3)" || exit 1

ENTRYPOINT ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
