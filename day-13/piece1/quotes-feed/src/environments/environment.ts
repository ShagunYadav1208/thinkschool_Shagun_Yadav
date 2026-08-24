export const environment = {
  // Relative, not absolute - goes through the dev-server proxy
  // (proxy.conf.json -> http://localhost:5310) instead of calling the real
  // Week-1 QuotesApi directly from the browser. Direct calls hit a real CORS
  // wall: QuotesApi has no Access-Control-Allow-Origin configured, and
  // fixing that means editing that project, which this exercise's brief
  // says not to do. The proxy sidesteps it entirely - the browser only ever
  // talks to the dev server's own origin.
  apiBaseUrl: '/api/quotes/',
};
