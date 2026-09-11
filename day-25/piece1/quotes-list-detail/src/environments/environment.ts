export const environment = {
  // Relative - goes through the dev-server proxy (proxy.conf.json ->
  // http://localhost:5116) instead of calling it directly from the browser.
  apiBaseUrl: '/api/quotes/',
  // Empty locally - jobs.service.ts and service-bus.service.ts build their
  // base URLs as `${apiOrigin}/api/...`, and an empty origin plus a
  // leading slash is still routed through the same dev-server proxy above.
  apiOrigin: '',
  // Day 25: real Entra ID (Azure AD) sign-in. clientId/tenantId/apiScope are
  // public identifiers, not secrets - safe to commit (see README "Why these
  // IDs are safe to commit"). Flip `authEnabled` to false (and QuotesApi's
  // appsettings.json Auth:Enabled) to fall back to no-auth for local testing
  // without touching either app registration.
  authEnabled: true,
  msal: {
    clientId: '335b9c06-58f9-4bc3-8732-b3f16bcf39e6',
    authority: 'https://login.microsoftonline.com/8d46a076-d093-416d-a57b-8692cde13bf8',
    redirectUri: 'http://localhost:4200',
    apiScope: 'api://fbc2f15a-e32e-4e04-9a55-9dc796093009/access_as_user',
  },
};
