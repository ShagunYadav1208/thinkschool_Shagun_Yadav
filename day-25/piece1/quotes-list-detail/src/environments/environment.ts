export const environment = {
  // Relative - goes through the dev-server proxy (proxy.conf.json -> the
  // real Week-1 QuotesApi on http://localhost:5310) instead of calling it
  // directly from the browser, which hits a real CORS wall (that project
  // has no CORS policy, and this brief says not to modify it).
  apiBaseUrl: '/api/quotes/',
  // Empty locally - jobs.service.ts and service-bus.service.ts build their
  // base URLs as `${apiOrigin}/api/...`, and an empty origin plus a
  // leading slash is still routed through the same dev-server proxy above.
  apiOrigin: '',
  // Day 25: real Entra ID (Azure AD) sign-in, feature-flagged off for now -
  // see README "Entra ID app auth (feature-flagged, off by default)".
  // clientId/tenantId/apiScope are public identifiers, not secrets - safe to
  // commit even while disabled (see README "Why these IDs are safe to
  // commit"). Flip `authEnabled` to true (and QuotesApi's appsettings.json
  // Auth:Enabled) once this student's Azure subscription is reactivated.
  authEnabled: false,
  msal: {
    clientId: '335b9c06-58f9-4bc3-8732-b3f16bcf39e6',
    authority: 'https://login.microsoftonline.com/8d46a076-d093-416d-a57b-8692cde13bf8',
    redirectUri: 'http://localhost:4200',
    apiScope: 'api://fbc2f15a-e32e-4e04-9a55-9dc796093009/access_as_user',
  },
};
