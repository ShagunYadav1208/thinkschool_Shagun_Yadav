namespace ParkFlow.Api.Security;

/// <summary>
/// These headers are cheap, have no functional downside, and are exactly what a passive scanner
/// (OWASP ZAP baseline, day-27) flags as missing on every response — see day-27's
/// SECURITY-TESTING.md.
/// </summary>
public static class SecurityHeadersMiddleware
{
    // Day 27's original CSP was "default-src 'none'" on the reasoning that "a JSON API serves no
    // HTML" — true until Day 32 added a real browser frontend (wwwroot/) served from this same
    // origin. 'none' silently blocked that frontend's own script, its own stylesheet, and the
    // Google Fonts stylesheet it loads — caught by actually loading the page in a browser, not by
    // any test in this suite (every existing test talks to the API directly, never renders HTML).
    // unpkg.com (Leaflet) and OpenStreetMap's tile servers were added the same way, for the same
    // reason, when the frontend grew an interactive map — each addition is the specific host the
    // frontend actually loads from, not a broad allowance.
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' https://unpkg.com; " +
        "style-src 'self' https://fonts.googleapis.com https://unpkg.com; " +
        "font-src https://fonts.gstatic.com; " +
        "connect-src 'self'; " +
        "img-src 'self' https://*.tile.openstreetmap.org data:; " +
        "frame-ancestors 'none'";

    public static IApplicationBuilder UseParkFlowSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
            context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
            // Day 32: the frontend is same-origin only (no cross-origin caller exists), so this
            // stays "same-origin" unchanged — a browser tab loading wwwroot/index.html *is* the
            // same origin, this only ever mattered for a hypothetical different-origin caller.
            context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
            await next();
        });
    }
}
