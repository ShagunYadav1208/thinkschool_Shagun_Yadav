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
  // Same app registration as environment.ts, different redirectUri - Entra ID
  // validates the redirect URI against the exact list registered on the SPA
  // app (see README "MI wiring + Entra ID app registrations"), so production
  // needs its own entry added there, not just a different value here.
  // authEnabled flipped to true to match day-25/piece1's current state, but
  // this piece has never actually been deployed (subscription blocker - see
  // README "Current status") - `redirectUri` below is still whatever URL an
  // earlier exercise last registered, not one that's been verified for
  // THIS piece's own deployment. Update it to the real hosting URL (and
  // register that URL on the SPA app registration) before this build is
  // actually used anywhere.
  authEnabled: true,
  msal: {
    clientId: '335b9c06-58f9-4bc3-8732-b3f16bcf39e6',
    authority: 'https://login.microsoftonline.com/8d46a076-d093-416d-a57b-8692cde13bf8',
    redirectUri: 'https://black-desert-0fde3f100.7.azurestaticapps.net',
    apiScope: 'api://fbc2f15a-e32e-4e04-9a55-9dc796093009/access_as_user',
  },
};
