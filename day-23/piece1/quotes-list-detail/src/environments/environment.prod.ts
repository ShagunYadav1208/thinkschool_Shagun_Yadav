export const environment = {
  // Absolute, cross-origin: the production build runs as static files on
  // Azure Static Web Apps, with no dev-server proxy to hide behind. It calls
  // the QuotesApi App Service directly - CORS on that side (Cors:AllowedOrigin
  // in appsettings.Production.json) is what makes this browser call legal,
  // not this URL. Filled in with the real App Service hostname at deploy
  // time - see infra/README.md.
  apiBaseUrl: 'https://syquotes17-api.azurewebsites.net/api/quotes/',
  // Same App Service, no path - jobs.service.ts and service-bus.service.ts
  // build their base URLs as `${apiOrigin}/api/...`. A real bug this exercise
  // caught live: both of those services originally hardcoded a relative
  // '/api/...' path (copied from the pre-existing apiBaseUrl pattern without
  // noticing it only worked because THAT one goes through the dev-proxy
  // locally and gets its production value read from this very file) - on the
  // deployed static site there's no dev-proxy, so a relative path resolved
  // against the SWA's own origin instead of the API, silently hit the SPA's
  // navigation fallback, and got back index.html instead of JSON. See
  // verification-log.md.
  apiOrigin: 'https://syquotes17-api.azurewebsites.net',
};
