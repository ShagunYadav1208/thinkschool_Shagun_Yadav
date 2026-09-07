import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { timer } from 'rxjs';
import { retry } from 'rxjs/operators';

const MAX_RETRIES = 3;
const BASE_DELAY_MS = 300;

/**
 * status 0 = request never reached the server (network drop, CORS
 * preflight failure); 5xx = the server itself failed. Both are worth
 * retrying. A 4xx (including our 400 ValidationProblemDetails responses) is
 * never retried - `page=0` is going to be invalid the second time too, so
 * retrying it just delays showing the user the real error.
 */
function isRetryable(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 0 || error.status >= 500);
}

/**
 * Retries idempotent GETs with exponential backoff (300ms, 600ms, 1200ms) on
 * network/5xx failures only. POST/PATCH/DELETE pass through untouched -
 * retrying a non-idempotent write risks double-submitting it.
 */
export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') {
    return next(req);
  }

  return next(req).pipe(
    retry({
      count: MAX_RETRIES,
      delay: (error, retryCount) => {
        if (!isRetryable(error)) {
          throw error;
        }
        return timer(BASE_DELAY_MS * 2 ** (retryCount - 1));
      },
    })
  );
};
