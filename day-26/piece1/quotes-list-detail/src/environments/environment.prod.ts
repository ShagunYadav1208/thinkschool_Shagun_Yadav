export const environment = {
  // Absolute, cross-origin: the production build runs as static files on
  // Azure Static Web Apps, with no dev-server proxy to hide behind. It calls
  // the QuotesApi App Service directly - CORS on that side (Cors:AllowedOrigin
  // in appsettings.Production.json) is what makes this browser call legal,
  // not this URL. Filled in with the real App Service hostname at deploy
  // time - see infra/README.md.
  apiBaseUrl: 'https://syquotes26dev-api.azurewebsites.net/api/quotes/',
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
  apiOrigin: 'https://syquotes26dev-api.azurewebsites.net',
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
    // Own app registration (syquotes26dev-spa), not day-25's 335b9c06 - that
    // one is owned by a different Entra account than the one this deployment
    // runs under, so its redirect-URI allow-list couldn't be edited here.
    // Targets the SAME API app (fbc2f15a) and the SAME exposed scope
    // (access_as_user) via requiredResourceAccess - the API only validates
    // audience/tenant on the token, not which client requested it, so this
    // works identically from the backend's point of view.
    clientId: 'c2c20640-ec14-4ef2-aa98-c63889c016f5',
    authority: 'https://login.microsoftonline.com/8d46a076-d093-416d-a57b-8692cde13bf8',
    redirectUri: 'https://gray-wave-009757500.5.azurestaticapps.net',
    apiScope: 'api://fbc2f15a-e32e-4e04-9a55-9dc796093009/access_as_user',
  },
};
