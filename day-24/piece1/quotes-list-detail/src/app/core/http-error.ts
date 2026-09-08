import { HttpErrorResponse } from '@angular/common/http';

/**
 * Typed shape every failed request ends up as, once errorMappingInterceptor
 * has run. Callers never touch HttpErrorResponse directly.
 */
export interface AppHttpError {
  status: number;
  friendlyMessage: string;
  /** Field -> messages, straight from a real ValidationProblemDetails `errors` map. Null when the body wasn't shaped like one (e.g. a network failure, a plain 404). */
  fieldErrors: Record<string, string[]> | null;
  raw: unknown;
}

/**
 * Matches the exact shape `Results.ValidationProblem(...)` sends from the
 * real Week-1 QuotesApi - confirmed live via curl against
 * GET /api/quotes?page=0&size=5:
 * {"type":"...","title":"One or more validation errors occurred.",
 *  "status":400,"errors":{"page":["Page must be greater than 0."]},"traceId":"..."}
 */
interface ProblemDetailsBody {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}

function isProblemDetailsBody(body: unknown): body is ProblemDetailsBody {
  return typeof body === 'object' && body !== null && ('title' in body || 'errors' in body);
}

function firstFieldError(errors: Record<string, string[]>): string | null {
  const firstKey = Object.keys(errors)[0];
  return firstKey ? errors[firstKey][0] : null;
}

/**
 * Maps a raw HttpErrorResponse to the typed AppHttpError callers work with.
 * On a 4xx with a ValidationProblemDetails body, the friendly message is the
 * first field error (e.g. "Size must be between 1 and 100.") rather than the
 * generic "One or more validation errors occurred." title - that's the part
 * a user actually needs to see. Anything else (network failure, a plain
 * empty-body 404, a 500 that survived retries) falls back to a generic
 * message keyed off the status.
 */
export function toAppHttpError(error: HttpErrorResponse): AppHttpError {
  const body = error.error;

  if (isProblemDetailsBody(body)) {
    const fieldErrors = body.errors ?? null;
    const friendlyMessage =
      (fieldErrors && firstFieldError(fieldErrors)) ?? body.detail ?? body.title ?? 'Request failed.';

    return {
      status: error.status,
      friendlyMessage,
      fieldErrors,
      raw: body,
    };
  }

  if (error.status === 0) {
    return {
      status: 0,
      friendlyMessage: "Can't reach the server. Check your connection and try again.",
      fieldErrors: null,
      raw: error.error,
    };
  }

  if (error.status === 404) {
    return {
      status: 404,
      friendlyMessage: 'Not found.',
      fieldErrors: null,
      raw: error.error,
    };
  }

  return {
    status: error.status,
    friendlyMessage: 'Something went wrong. Please try again.',
    fieldErrors: null,
    raw: error.error,
  };
}
