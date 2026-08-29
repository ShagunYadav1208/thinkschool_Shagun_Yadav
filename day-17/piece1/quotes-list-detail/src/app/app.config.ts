import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withViewTransitions } from '@angular/router';
import { authInterceptor } from './core/auth.interceptor';
import { errorMappingInterceptor } from './core/error-mapping.interceptor';
import { retryInterceptor } from './core/retry.interceptor';
import { quotesRoutes } from './quotes-routing/quotes.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    // Order matters: requests flow auth -> errorMapping -> retry -> backend;
    // responses flow the other way, so retryInterceptor sees the raw
    // HttpErrorResponse first (and can retry on it), and only once it gives
    // up does errorMappingInterceptor turn that into an AppHttpError.
    provideHttpClient(withInterceptors([authInterceptor, errorMappingInterceptor, retryInterceptor])),
    // withComponentInputBinding binds the `:id` segment straight onto
    // QuoteDetailRoute's `id` input; withViewTransitions wraps every
    // navigation in document.startViewTransition() so the shared
    // view-transition-name on a quote's title actually morphs.
    provideRouter(quotesRoutes, withComponentInputBinding(), withViewTransitions()),
  ]
};
