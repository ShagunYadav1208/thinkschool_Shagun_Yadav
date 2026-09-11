export const environment = {
  // Absolute, cross-origin: the production build runs as static files on an
  // Azure Storage static website, with no dev-server proxy to hide behind.
  // It calls the QuotesApi App Service directly - CORS on that side
  // (Cors__AllowedOrigin, set as a real App Service setting by
  // infra/main.bicep, not this file) is what makes this browser call legal.
  apiBaseUrl: 'https://syquotes25dev-api.azurewebsites.net/api/quotes/',
  // Same App Service, no path - jobs.service.ts and service-bus.service.ts
  // build their base URLs as `${apiOrigin}/api/...`.
  apiOrigin: 'https://syquotes25dev-api.azurewebsites.net',
  // Day 25: real Entra ID (Azure AD) sign-in - same app registrations as
  // environment.ts, only redirectUri differs. Must exactly match one of the
  // SPA app registration's registered redirect URIs - this storage static
  // website's own URL was added there for exactly this (see infra/deploy.md).
  authEnabled: true,
  msal: {
    clientId: '335b9c06-58f9-4bc3-8732-b3f16bcf39e6',
    authority: 'https://login.microsoftonline.com/8d46a076-d093-416d-a57b-8692cde13bf8',
    redirectUri: 'https://syquotes25devfe.z7.web.core.windows.net',
    apiScope: 'api://fbc2f15a-e32e-4e04-9a55-9dc796093009/access_as_user',
  },
};
