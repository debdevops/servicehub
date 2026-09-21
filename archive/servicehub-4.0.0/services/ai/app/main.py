"""FastAPI skeleton for the ServiceHub AI service.

/analyze clusters a batch of FeatureRecords by error signature; see
app/clustering.py for the algorithm.
"""

from fastapi import FastAPI

from app.clustering import analyze_records
from app.models import AnalyzeRequest, AnalyzeResponse, HealthResponse

VERSION = "0.1.0"

app = FastAPI(title="ServiceHub AI Service", version=VERSION)


@app.get("/health", response_model=HealthResponse)
def health() -> HealthResponse:
    return HealthResponse(status="ok", version=VERSION, ready=True)


@app.post("/analyze", response_model=AnalyzeResponse)
def analyze(request: AnalyzeRequest) -> AnalyzeResponse:
    result = analyze_records(request.records)
    return AnalyzeResponse(
        clusters=result["clusters"],
        singletons=result["singletons"],
        method=result["method"],
        explanation=None,
    )
