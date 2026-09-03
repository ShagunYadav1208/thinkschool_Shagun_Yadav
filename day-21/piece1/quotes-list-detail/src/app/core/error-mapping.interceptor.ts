import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { toAppHttpError } from './http-error';

/**
 * Registered BETWEEN authInterceptor and retryInterceptor (see
 * app.config.ts) so it only sees a response once retryInterceptor has
 * exhausted its attempts - it maps the terminal HttpErrorResponse into the
 * typed AppHttpError every consumer of QuotesService actually works with,
 * not an error that's about to be retried anyway.
 */
export const errorMappingInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) {
        return throwError(() => toAppHttpError(error));
      }
      return throwError(() => error);
    })
  );
