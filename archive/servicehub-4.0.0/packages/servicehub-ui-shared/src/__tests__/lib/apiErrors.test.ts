import { describe, it, expect } from 'vitest';
import { extractApiError } from '../../lib/api/errors';

describe('extractApiError', () => {
  const FALLBACK = 'Failed to do the thing';

  it('prefers ProblemDetails detail — the Result<T> expected-failure path', () => {
    const error = {
      response: {
        status: 400,
        data: {
          status: 400,
          title: 'Bad Request',
          detail: 'Namespace is Production — replay is disabled.',
        },
      },
      message: 'Request failed with status code 400',
    };

    expect(extractApiError(error, FALLBACK)).toBe('Namespace is Production — replay is disabled.');
  });

  it('falls back to ErrorResponse message — the unhandled-exception path', () => {
    // ErrorHandlingMiddleware serializes { code, message, correlationId }, with no `detail`.
    const error = {
      response: {
        status: 500,
        data: {
          code: 'Internal.UnexpectedError',
          message: 'An internal error occurred. Please try again later.',
          correlationId: 'abc-123',
        },
      },
      message: 'Request failed with status code 500',
    };

    expect(extractApiError(error, FALLBACK)).toBe(
      'An internal error occurred. Please try again later.'
    );
  });

  it('falls back to the ProblemDetails title when detail is absent', () => {
    const error = {
      response: { status: 409, data: { status: 409, title: 'Conflict' } },
      message: 'Request failed with status code 409',
    };

    expect(extractApiError(error, FALLBACK)).toBe('Conflict');
  });

  it("uses the axios message only when the server explained nothing", () => {
    const error = { response: { status: 502, data: {} }, message: 'Network Error' };

    expect(extractApiError(error, FALLBACK)).toBe('Network Error');
  });

  it('uses the caller fallback when there is no error information at all', () => {
    expect(extractApiError({}, FALLBACK)).toBe(FALLBACK);
    expect(extractApiError(undefined, FALLBACK)).toBe(FALLBACK);
    expect(extractApiError(null, FALLBACK)).toBe(FALLBACK);
  });

  it('ignores blank and whitespace-only server fields', () => {
    const error = {
      response: { status: 400, data: { detail: '   ', title: '' } },
      message: 'Request failed with status code 400',
    };

    expect(extractApiError(error, FALLBACK)).toBe('Request failed with status code 400');
  });

  it('never returns a raw axios status string when the server sent a detail', () => {
    // This is the exact regression P1-1 describes.
    const error = {
      response: { status: 400, data: { detail: 'Sequence number 42 is no longer in the queue.' } },
      message: 'Request failed with status code 400',
    };

    expect(extractApiError(error, FALLBACK)).not.toContain('status code');
  });
});
