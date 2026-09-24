import axios, { AxiosError } from 'axios'

/**
 * The HTTP client. One instance, one place where an API failure becomes something a screen can
 * render honestly.
 *
 * Same origin in production and proxied in development (ADR-0014 D3), so there is no base URL to
 * configure and no CORS to get wrong.
 */
export const api = axios.create({
  baseURL: '/api/v1',
  timeout: 30_000,
  headers: { 'Content-Type': 'application/json' },
})

/** A failure, reduced to what a screen needs to say something true about it. */
export interface ApiProblem {
  /** The stable machine-readable code from the API's ProblemDetails. */
  readonly code: string
  /** One sentence for a person. Never a raw status code, never a stack trace. */
  readonly message: string
  readonly status?: number
  /** Ties this failure to the server logs. */
  readonly correlationId?: string
}

const NETWORK_FAILURE: ApiProblem = {
  code: 'network_unreachable',
  message: 'ServiceHub could not reach its API. Check that the service is running.',
}

/**
 * Turns anything thrown by the client into an {@link ApiProblem}.
 *
 * Every error path in the UI goes through here, so no screen ever has to decide what to show for
 * an unexpected shape — and no screen shows a bare status code.
 */
export function toProblem(error: unknown): ApiProblem {
  if (!axios.isAxiosError(error)) {
    return { code: 'unexpected_failure', message: 'Something went wrong.' }
  }

  const axiosError = error as AxiosError<Record<string, unknown>>
  const data = axiosError.response?.data

  if (!axiosError.response) {
    return NETWORK_FAILURE
  }

  const code = typeof data?.code === 'string' ? data.code : 'unexpected_failure'
  const detail = typeof data?.detail === 'string' ? data.detail : undefined
  const title = typeof data?.title === 'string' ? data.title : undefined
  const correlationId = typeof data?.correlationId === 'string' ? data.correlationId : undefined

  return {
    code,
    message: detail ?? title ?? 'ServiceHub could not complete that request.',
    status: axiosError.response.status,
    correlationId,
  }
}
