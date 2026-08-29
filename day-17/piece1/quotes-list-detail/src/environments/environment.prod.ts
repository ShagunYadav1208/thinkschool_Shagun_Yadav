export const environment = {
  // Absolute, cross-origin: the production build runs as static files on
  // Azure Static Web Apps, with no dev-server proxy to hide behind. It calls
  // the QuotesApi App Service directly - CORS on that side (Cors:AllowedOrigin
  // in appsettings.Production.json) is what makes this browser call legal,
  // not this URL. Filled in with the real App Service hostname at deploy
  // time - see infra/README.md.
  apiBaseUrl: 'https://syquotes17-api.azurewebsites.net/api/quotes/',
};
