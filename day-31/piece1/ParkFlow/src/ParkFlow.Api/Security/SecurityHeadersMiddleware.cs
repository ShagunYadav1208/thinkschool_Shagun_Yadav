namespace ParkFlow.Api.Security;

/// <summary>
/// A JSON API serves no HTML, but these headers are cheap, have no functional downside, and are
/// exactly what a passive scanner (OWASP ZAP baseline, this pass) flags as missing on every
/// response — see day-27's SECURITY-TESTING.md.
/// </summary>
public static class SecurityHeadersMiddleware
{
    public static IApplicationBuilder UseParkFlowSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
            // No browser client of this API exists today (see THREAT-MODEL.md) - same-origin is
            // the strictest setting and costs nothing until a legitimate cross-origin caller
            // shows up, at which point it should become an explicit, reviewed relaxation.
            context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
            await next();
        });
    }
}
