import { HttpInterceptorFn } from '@angular/common/http';

const TOKEN_STORAGE_KEY = 'auth_token';

/**
 * Attaches a bearer token to every outgoing request. The real Week-1
 * QuotesApi has no auth of its own (confirmed live - no 401 from any
 * endpoint), so there's nothing to fetch this from; a placeholder token
 * proves the interceptor actually rewrites every request, which is the part
 * this exercise is about.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = localStorage.getItem(TOKEN_STORAGE_KEY) ?? 'demo-token';

  return next(
    req.clone({
      setHeaders: { Authorization: `Bearer ${token}` },
    })
  );
};
