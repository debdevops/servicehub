import { ApiError } from './types';

/**
 * Extracts the most specific human-readable explanation the API returned.
 *
 * The backend emits two distinct error shapes, and both are legitimate:
 *
 *  1. RFC 7807 `ProblemDetails` from `ApiControllerBase.CreateProblemDetails` — the
 *     `Result<T>` expected-failure path, which is the overwhelming majority of errors
 *     (production-namespace replay blocked, provider capability unsupported, validation
 *     rejected, provider 503). The explanation lives in `detail`.
 *
 *  2. `ErrorResponse` from `ErrorHandlingMiddleware` — genuinely unhandled exceptions
 *     only. It serializes to `{ code, message, correlationId }`, so the explanation
 *     lives in `message`.
 *
 * `detail` is therefore tried first and `message` second; reading only one of them
 * silently discards the server's explanation for the other half of the failure space.
 * Reading `message` alone was the v3.6.0 P1-1 defect: every ProblemDetails failure fell
 * through to Axios's own `"Request failed with status code 400"`.
 *
 * `title` is tried before the Axios fallback because a ProblemDetails without a `detail`
 * still carries a meaningful title ("Not Found", "Conflict").
 */
export function extractApiError(error: unknown, fallback: string): string {
  const e = error as ApiError | undefined;
  const data = e?.response?.data;

  const candidates = [data?.detail, data?.message, data?.title, e?.message];

  for (const candidate of candidates) {
    if (typeof candidate === 'string' && candidate.trim().length > 0) {
      return candidate;
    }
  }

  return fallback;
}
