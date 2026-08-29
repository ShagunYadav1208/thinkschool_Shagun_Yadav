import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { QuotesService } from './quotes.service';
import { environment } from '../environments/environment';
import { authInterceptor } from './core/auth.interceptor';
import { errorMappingInterceptor } from './core/error-mapping.interceptor';
import { retryInterceptor } from './core/retry.interceptor';
import { AppHttpError } from './core/http-error';

/**
 * Characterization test: pins the REAL Week-1 QuotesApi contract
 * (day-1/piece3/QuotesApi), confirmed live via curl against
 * http://localhost:5310 before any of this file or the UI was written -
 * not guessed from the controller source alone.
 *
 *   curl "http://localhost:5310/api/quotes?page=1&size=2"
 *   -> 200 [{"id":17,"author":"Ada Lovelace","text":"..."}, ...]
 *
 *   curl "http://localhost:5310/api/quotes?page=0&size=5"
 *   -> 400 application/problem+json
 *      {"type":"...","title":"One or more validation errors occurred.",
 *       "status":400,"errors":{"page":["Page must be greater than 0."]},
 *       "traceId":"..."}
 *
 *   curl "http://localhost:5310/api/quotes?page=1&size=500"
 *   -> 400 {"errors":{"size":["Size must be between 1 and 100."]}, ...}
 *
 * Also exercises the interceptor pipeline wired in app.config.ts: auth
 * header, retry-with-backoff on idempotent GETs (5xx/network only, never
 * 4xx, never non-GET), and ProblemDetails -> AppHttpError mapping.
 */
describe('QuotesService against the real Week-1 API contract', () => {
  let service: QuotesService;
  let httpMock: HttpTestingController;

  const endpoint = environment.apiBaseUrl;

  const REAL_PAGE: ReadonlyArray<{ id: number; author: string; text: string }> = [
    { id: 17, author: 'Ada Lovelace', text: 'That brain of mine is something more than merely mortal.' },
    {
      id: 18,
      author: 'Grace Hopper',
      text: 'The most dangerous phrase in the language is: we have always done it this way.',
    },
  ];

  const PAGE_VALIDATION_PROBLEM = {
    type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
    title: 'One or more validation errors occurred.',
    status: 400,
    errors: { page: ['Page must be greater than 0.'] },
    traceId: '00-579a112086e77587297bd5a9ae458ebd-97c25e887eba598e-00',
  };

  const SIZE_VALIDATION_PROBLEM = {
    type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
    title: 'One or more validation errors occurred.',
    status: 400,
    errors: { size: ['Size must be between 1 and 100.'] },
    traceId: '00-4ceb1327fc00e3b3ff4ac2843df79095-27d27f3ce53723ca-00',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor, errorMappingInterceptor, retryInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(QuotesService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('GET /api/quotes/?page=1&size=2 returns Quote[] shaped exactly {id, author, text}', async () => {
    const resultPromise = firstValueFrom(service.getQuotesPage(1, 2));

    const req = httpMock.expectOne(
      (r) => r.url === endpoint && r.params.get('page') === '1' && r.params.get('size') === '2'
    );
    expect(req.request.method).toBe('GET');
    req.flush(REAL_PAGE);

    const result = await resultPromise;
    expect(result).toEqual(REAL_PAGE);
    // No extra fields (e.g. createdAt) snuck in - the whole point of pinning
    // the real shape instead of a loosely-typed guess.
    expect(Object.keys(result[0]).sort()).toEqual(['author', 'id', 'text']);
  });

  it('adds a Bearer Authorization header to the request', async () => {
    const resultPromise = firstValueFrom(service.getQuotesPage(1, 10));
    const req = httpMock.expectOne((r) => r.url === endpoint);
    expect(req.request.headers.get('Authorization')).toMatch(/^Bearer .+/);
    req.flush(REAL_PAGE);
    await resultPromise;
  });

  it('maps a real page=0 ValidationProblemDetails 400 to a friendly AppHttpError', async () => {
    const resultPromise = firstValueFrom(service.getQuotesPage(0, 5));

    const req = httpMock.expectOne((r) => r.url === endpoint);
    req.flush(PAGE_VALIDATION_PROBLEM, { status: 400, statusText: 'Bad Request' });

    await expect(resultPromise).rejects.toMatchObject({
      status: 400,
      friendlyMessage: 'Page must be greater than 0.',
      fieldErrors: { page: ['Page must be greater than 0.'] },
    } satisfies Partial<AppHttpError>);
  });

  it('maps a real size=500 ValidationProblemDetails 400 to a friendly AppHttpError', async () => {
    const resultPromise = firstValueFrom(service.getQuotesPage(1, 500));

    const req = httpMock.expectOne((r) => r.url === endpoint);
    req.flush(SIZE_VALIDATION_PROBLEM, { status: 400, statusText: 'Bad Request' });

    await expect(resultPromise).rejects.toMatchObject({
      status: 400,
      friendlyMessage: 'Size must be between 1 and 100.',
      fieldErrors: { size: ['Size must be between 1 and 100.'] },
    } satisfies Partial<AppHttpError>);
  });

  it('does NOT retry a 400 - exactly one request is made', async () => {
    const resultPromise = firstValueFrom(service.getQuotesPage(0, 5));

    const req = httpMock.expectOne((r) => r.url === endpoint);
    req.flush(PAGE_VALIDATION_PROBLEM, { status: 400, statusText: 'Bad Request' });

    await expect(resultPromise).rejects.toMatchObject({ status: 400 });
    // afterEach's httpMock.verify() is the real assertion here - it fails
    // the test if any unexpected/unflushed request (i.e. a retry) exists.
  });

  it('retries an idempotent GET on a 500, then succeeds on the 2nd attempt', async () => {
    const resultPromise = firstValueFrom(service.getQuotesPage(1, 10));

    const attempt1 = httpMock.expectOne((r) => r.url === endpoint);
    attempt1.flush('boom', { status: 500, statusText: 'Internal Server Error' });

    // Real backoff delay (300ms base) - waited out for real rather than
    // faked, since this app has no zone.js (zoneless) and Angular's
    // vitest-based test builder doesn't ship fakeAsync/tick for it.
    await new Promise((resolve) => setTimeout(resolve, 400));

    const attempt2 = httpMock.expectOne((r) => r.url === endpoint);
    attempt2.flush(REAL_PAGE);

    const result = await resultPromise;
    expect(result).toEqual(REAL_PAGE);
  }, 10_000);

  it('maps a real GET /api/quotes/{id} 404 (empty body, confirmed live via curl) to a friendly AppHttpError', async () => {
    const resultPromise = firstValueFrom(service.getQuoteById(999999));

    const req = httpMock.expectOne(`${endpoint}999999`);
    // The real API sends Content-Length: 0 with a 404 - no JSON body at all,
    // unlike the 400s above. flush('') reproduces that exactly.
    req.flush('', { status: 404, statusText: 'Not Found' });

    await expect(resultPromise).rejects.toMatchObject({
      status: 404,
      friendlyMessage: 'Not found.',
      fieldErrors: null,
    } satisfies Partial<AppHttpError>);
  });

  it('gives up after exhausting retries on a persistent 500, surfacing the final AppHttpError', async () => {
    const resultPromise = firstValueFrom(service.getQuotesPage(1, 10));

    // 1 initial attempt + 3 retries (300ms, 600ms, 1200ms backoff) = 4 total, all fail.
    for (let attempt = 1; attempt <= 4; attempt++) {
      const req = httpMock.expectOne((r) => r.url === endpoint);
      req.flush('boom', { status: 500, statusText: 'Internal Server Error' });
      if (attempt < 4) {
        await new Promise((resolve) => setTimeout(resolve, 300 * 2 ** (attempt - 1) + 100));
      }
    }

    await expect(resultPromise).rejects.toMatchObject({
      status: 500,
      friendlyMessage: 'Something went wrong. Please try again.',
      fieldErrors: null,
    } satisfies Partial<AppHttpError>);
  }, 10_000);

  it('does NOT retry a POST even on a 500', async () => {
    const resultPromise = firstValueFrom(service.createQuote({ author: 'Test', text: 'Test quote' }));

    const req = httpMock.expectOne((r) => r.url === endpoint && r.method === 'POST');
    req.flush('boom', { status: 500, statusText: 'Internal Server Error' });

    await expect(resultPromise).rejects.toMatchObject({ status: 500 });
    // httpMock.verify() in afterEach confirms no second attempt was made.
  });

  /**
   * DELETE /api/quotes/{id} - confirmed live: created a throwaway quote via
   * POST, deleted it (204 No Content), then deleted the SAME id again
   * (404, empty body - not a 204). A real "delete an id that's already
   * gone" case, not guessed.
   */
  it('DELETE /api/quotes/{id} resolves with no error on a real 204', async () => {
    const resultPromise = firstValueFrom(service.deleteQuote(33));

    const req = httpMock.expectOne(`${endpoint}33`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });

    await expect(resultPromise).resolves.toBeNull();
  });

  it('maps a real second-DELETE-of-the-same-id 404 (empty body) to a friendly AppHttpError', async () => {
    const resultPromise = firstValueFrom(service.deleteQuote(33));

    const req = httpMock.expectOne(`${endpoint}33`);
    req.flush('', { status: 404, statusText: 'Not Found' });

    await expect(resultPromise).rejects.toMatchObject({
      status: 404,
      friendlyMessage: 'Not found.',
      fieldErrors: null,
    } satisfies Partial<AppHttpError>);
  });

  it('does NOT retry a DELETE even on a 500', async () => {
    const resultPromise = firstValueFrom(service.deleteQuote(33));

    const req = httpMock.expectOne((r) => r.url === `${endpoint}33` && r.method === 'DELETE');
    req.flush('boom', { status: 500, statusText: 'Internal Server Error' });

    await expect(resultPromise).rejects.toMatchObject({ status: 500 });
    // httpMock.verify() in afterEach confirms no second attempt was made.
  });
});
