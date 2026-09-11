import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { QuoteManagementStore } from './quote-management-store';
import { environment } from '../environments/environment';
import { errorMappingInterceptor } from './core/error-mapping.interceptor';
import { retryInterceptor } from './core/retry.interceptor';

/**
 * Grounded in the real Week-1 QuotesApi contract (day-1/piece3/QuotesApi),
 * confirmed live via curl against http://localhost:5310 before writing this
 * file:
 *   curl "http://localhost:5310/api/quotes/?page=1&size=5"   -> 200, 5 quotes (ids 17,18,19,22,26)
 *   curl "http://localhost:5310/api/quotes/?page=999&size=5" -> 200 []  (empty page, not a 404)
 *   curl "http://localhost:5310/api/quotes/?page=0&size=5"   -> 400 ValidationProblemDetails
 *   curl -X DELETE ".../api/quotes/33" (real id)              -> 204 No Content
 *   curl -X DELETE ".../api/quotes/33" (same id again)        -> 404 empty body
 */
describe('QuoteManagementStore against the real Week-1 API contract', () => {
  let store: QuoteManagementStore;
  let httpMock: HttpTestingController;

  const endpoint = environment.apiBaseUrl;
  const PAGE_1 = [
    { id: 17, author: 'Ada Lovelace', text: 'That brain of mine is something more than merely mortal.' },
    { id: 18, author: 'Grace Hopper', text: 'The most dangerous phrase in the language is: we have always done it this way.' },
    { id: 19, author: 'Grace Hopper', text: 'A ship in port is safe, but that is not what ships are built for.' },
    { id: 22, author: 'Sumit Sharma', text: 'Har Burai mein se ek achchai zarur nikalti hai' },
    { id: 26, author: 'Mark Twain', text: "If you tell the truth, you don't have to remember anything." },
  ];
  const PAGE_2 = [
    { id: 31, author: 'Author 31', text: 'Quote 31' },
    { id: 32, author: 'Author 32', text: 'Quote 32' },
  ];
  const PAGE_VALIDATION_PROBLEM = {
    type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
    title: 'One or more validation errors occurred.',
    status: 400,
    errors: { page: ['Page must be greater than 0.'] },
    traceId: '00-142d2303513f5ee77eb6425933eca9fe-d1a2657d36ba8293-00',
  };

  function expectPageRequest(page: number) {
    return httpMock.expectOne(
      (r) => r.url === endpoint && r.params.get('page') === String(page) && r.params.get('size') === '5'
    );
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorMappingInterceptor, retryInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    store = TestBed.inject(QuoteManagementStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('starts in loading, then moves to loaded with page 1 (5 real quotes)', () => {
    store.start();
    expect(store.status()).toBe('loading');

    expectPageRequest(1).flush(PAGE_1);

    expect(store.status()).toBe('loaded');
    expect(store.quotes()).toEqual(PAGE_1);
    // A full page (5 = PAGE_SIZE) is the hasNext heuristic, since the API
    // gives no total count.
    expect(store.hasNext()).toBe(true);
    expect(store.hasPrevious()).toBe(false);
  });

  it('moving to a page beyond the real data (page 3, empty []) is a distinct "empty" state, not an error', () => {
    store.start();
    expectPageRequest(1).flush(PAGE_1);

    store.goToPage(3);
    expect(store.status()).toBe('loading');
    expectPageRequest(3).flush([]);

    expect(store.status()).toBe('empty');
    expect(store.hasNext()).toBe(false);
  });

  it('an invalid page (page=0) maps to the "error" state with the real ValidationProblemDetails message', () => {
    store.start();
    // Drain the initial page-1 fetch so the next assertion is only about page 0.
    expectPageRequest(1).flush(PAGE_1);

    store.goToPage(0);
    expectPageRequest(0).flush(PAGE_VALIDATION_PROBLEM, { status: 400, statusText: 'Bad Request' });

    expect(store.status()).toBe('error');
    expect(store.error()?.friendlyMessage).toBe('Page must be greater than 0.');
  });

  it('concurrent page navigation: a slow page-1 response arriving AFTER a fast page-2 response must not overwrite it (switchMap cancels the stale one)', () => {
    store.start();
    const req1 = expectPageRequest(1);

    // Immediately navigate to page 2 before page 1 has resolved - the real
    // edge this is meant to guard: a user double-clicking "Next" before the
    // first page even loaded.
    store.goToPage(2);
    const req2 = expectPageRequest(2);

    // Page 2 resolves FIRST even though it was requested second (plausible
    // if the server is momentarily slower on the earlier request).
    req2.flush(PAGE_2);
    expect(store.quotes()).toEqual(PAGE_2);

    // The stale page-1 request was already unsubscribed by switchMap the
    // moment page 2 was requested - Angular's HttpTestingController refuses
    // to flush a cancelled request at all, which is itself the proof that
    // it can never resurrect page 1's data over page 2's.
    expect(() => req1.flush(PAGE_1)).toThrow('Cannot flush a cancelled request.');
    expect(store.quotes()).toEqual(PAGE_2);
    expect(store.page()).toBe(2);
  });

  /**
   * THE BUG this replaced: a successful delete used to splice the deleted
   * item out of the local `quotes` array in place, with no refetch at all.
   * That broke pagination for real - `hasNext` is `quotes().length ===
   * PAGE_SIZE`, so removing one item locally made a genuinely-full page
   * look short, permanently disabling Next even though a real next page
   * still existed, and nothing ever shifted up from that next page to fill
   * the gap. Confirmed live in day-25, not guessed. Re-fetching the real
   * page from the server is what actually keeps pagination correct.
   */
  it('deleting a quote re-fetches the current page from the server (not a local splice)', () => {
    store.start();
    expectPageRequest(1).flush(PAGE_1);

    store.deleteQuote(17);
    const deleteReq = httpMock.expectOne(`${endpoint}17`);
    expect(deleteReq.request.method).toBe('DELETE');
    deleteReq.flush(null, { status: 204, statusText: 'No Content' });

    // The real API shifts a quote up from beyond this page once the deleted
    // one is gone - id 31 standing in for whatever that next real quote is.
    expectPageRequest(1).flush([
      { id: 18, author: 'Grace Hopper', text: 'The most dangerous phrase in the language is: we have always done it this way.' },
      { id: 19, author: 'Grace Hopper', text: 'A ship in port is safe, but that is not what ships are built for.' },
      { id: 22, author: 'Sumit Sharma', text: 'Har Burai mein se ek achchai zarur nikalti hai' },
      { id: 26, author: 'Mark Twain', text: "If you tell the truth, you don't have to remember anything." },
      { id: 31, author: 'Author 31', text: 'Quote 31' },
    ]);

    expect(store.quotes().map((q) => q.id)).toEqual([18, 19, 22, 26, 31]);
    expect(store.status()).toBe('loaded');
    // A genuinely full page again - Next must be live, not stuck disabled.
    expect(store.hasNext()).toBe(true);
  });

  /**
   * The other edge the refetch fix has to get right: deleting the LAST item
   * on a page beyond the first must not strand the user on a now-empty page
   * with both Next and Previous effectively dead - it should land them back
   * on the page that still has data.
   */
  it('deleting the only item on a page beyond the first goes back a page instead of showing empty', () => {
    store.start();
    expectPageRequest(1).flush(PAGE_1);

    store.goToPage(2);
    expectPageRequest(2).flush([{ id: 31, author: 'Author 31', text: 'Quote 31' }]);

    store.deleteQuote(31);
    httpMock.expectOne(`${endpoint}31`).flush(null, { status: 204, statusText: 'No Content' });

    // No refetch of the now-nonexistent page 2 - straight to page 1.
    expect(store.page()).toBe(1);
    expectPageRequest(1).flush(PAGE_1);
    expect(store.quotes()).toEqual(PAGE_1);
  });

  /**
   * THE BUG this test caught in the first draft: a double-click on the same
   * delete button called `deleteQuote(id)` twice with nothing guarding
   * against it, firing two DELETE requests for the same id. Fixed by
   * tracking in-flight ids in `deletingIds` and short-circuiting a repeat
   * call - proven here by asserting only ONE request ever reaches the API.
   */
  it('a double-click delete is deduped - calling deleteQuote twice for the same id fires only one DELETE request', () => {
    store.start();
    expectPageRequest(1).flush(PAGE_1);

    store.deleteQuote(17);
    expect(store.deletingIds().has(17)).toBe(true);
    store.deleteQuote(17); // the "second click" - must be a no-op while the first is in flight

    const deleteReqs = httpMock.match(`${endpoint}17`);
    expect(deleteReqs.length).toBe(1);

    deleteReqs[0].flush(null, { status: 204, statusText: 'No Content' });
    expectPageRequest(1).flush([
      { id: 18, author: 'Grace Hopper', text: 'The most dangerous phrase in the language is: we have always done it this way.' },
      { id: 19, author: 'Grace Hopper', text: 'A ship in port is safe, but that is not what ships are built for.' },
      { id: 22, author: 'Sumit Sharma', text: 'Har Burai mein se ek achchai zarur nikalti hai' },
      { id: 26, author: 'Mark Twain', text: "If you tell the truth, you don't have to remember anything." },
    ]);

    expect(store.quotes().map((q) => q.id)).toEqual([18, 19, 22, 26]);
    expect(store.deletingIds().has(17)).toBe(false);
  });

  /**
   * A second, related edge the client-side dedupe above can't fully cover:
   * two independent clients (a second browser tab, or a slow retry) can
   * still race the real server. Confirmed live via curl: deleting a real id
   * twice in a row returns 204 then 404 - the id genuinely is gone either
   * way. A 404 on delete has to be treated as "already gone" (success), not
   * a failure, or the UI would show an error for something that actually
   * worked.
   */
  it('a DELETE that 404s because the id is already gone is treated as success, not an error', () => {
    store.start();
    expectPageRequest(1).flush(PAGE_1);

    store.deleteQuote(17);
    const deleteReq = httpMock.expectOne(`${endpoint}17`);
    deleteReq.flush('', { status: 404, statusText: 'Not Found' });
    expectPageRequest(1).flush([
      { id: 18, author: 'Grace Hopper', text: 'The most dangerous phrase in the language is: we have always done it this way.' },
      { id: 19, author: 'Grace Hopper', text: 'A ship in port is safe, but that is not what ships are built for.' },
      { id: 22, author: 'Sumit Sharma', text: 'Har Burai mein se ek achchai zarur nikalti hai' },
      { id: 26, author: 'Mark Twain', text: "If you tell the truth, you don't have to remember anything." },
    ]);

    expect(store.quotes().map((q) => q.id)).toEqual([18, 19, 22, 26]);
    expect(store.status()).not.toBe('error');
    expect(store.error()).toBeNull();
  });

  it('a delete that fails for a real reason (e.g. a 500) DOES surface the error state', () => {
    store.start();
    expectPageRequest(1).flush(PAGE_1);

    store.deleteQuote(17);
    // retryInterceptor only retries GET (DELETE risks double-submitting a
    // non-idempotent write) - one 500 is the final answer, no retry to drain.
    const deleteReq = httpMock.expectOne(`${endpoint}17`);
    deleteReq.flush('boom', { status: 500, statusText: 'Internal Server Error' });

    expect(store.status()).toBe('error');
    expect(store.quotes().map((q) => q.id)).toEqual([17, 18, 19, 22, 26]);
    expect(store.deletingIds().has(17)).toBe(false);
  });
});
