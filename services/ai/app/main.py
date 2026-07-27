"""FastAPI skeleton for the ServiceHub AI service.

No algorithm yet — /analyze returns a correctly shaped stub response. The
real clustering/anomaly logic lands in P0-5 against this same contract.
"""

from fastapi import FastAPI

from app.models import AnalyzeRequest, AnalyzeResponse, HealthResponse

VERSION = "0.1.0"

app = FastAPI(title="ServiceHub AI Service", version=VERSION)


@app.get("/health", response_model=HealthResponse)
def health() -> HealthResponse:
    return HealthResponse(status="ok", version=VERSION, ready=True)


@app.post("/analyze", response_model=AnalyzeResponse)
def analyze(request: AnalyzeRequest) -> AnalyzeResponse:
    return AnalyzeResponse(clusters=[], explanation=None)
