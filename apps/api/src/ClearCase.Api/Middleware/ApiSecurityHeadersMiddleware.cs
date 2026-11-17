namespace ClearCase.Api.Middleware;

/// <summary>
/// Protective defaults for the JSON API. This application does not serve the Angular
/// SPA from its own origin; any future shared-origin frontend needs a separate CSP.
/// </summary>
public sealed class ApiSecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            return Task.CompletedTask;
        });
        await next(context);
    }
}
