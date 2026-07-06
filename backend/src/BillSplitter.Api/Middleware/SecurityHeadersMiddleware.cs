namespace BillSplitter.Api.Middleware;

/// <summary>
/// Sets the standard security header set on every response
/// (docs/10-security-privacy.md#transport-and-headers). The CSP is sized to the
/// same-origin SPA: everything from <c>'self'</c>, the SignalR socket over
/// <c>wss:</c>, and <c>data:</c> images for the QR data URI and the receipt blob.
/// HSTS is added separately (UseHsts) since it only makes sense once TLS is on.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "connect-src 'self' wss:; " +
        "img-src 'self' data:; " +
        "object-src 'none'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'";

    public Task InvokeAsync(HttpContext context)
    {
        // OnStarting so the headers survive an exception handler resetting the
        // response - they are stamped just before the first byte goes out.
        context.Response.OnStarting(static state =>
        {
            var headers = ((HttpContext)state).Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Content-Security-Policy"] = ContentSecurityPolicy;
            return Task.CompletedTask;
        }, context);

        return next(context);
    }
}
