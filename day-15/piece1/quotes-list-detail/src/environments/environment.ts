export const environment = {
  // Relative - goes through the dev-server proxy (proxy.conf.json -> the
  // real Week-1 QuotesApi on http://localhost:5310) instead of calling it
  // directly from the browser, which hits a real CORS wall (that project
  // has no CORS policy, and this brief says not to modify it).
  apiBaseUrl: '/api/quotes/',
};
