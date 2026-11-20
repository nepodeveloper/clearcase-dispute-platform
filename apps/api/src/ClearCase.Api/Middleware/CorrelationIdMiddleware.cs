using System.Text.RegularExpressions;

namespace ClearCase.Api.Middleware;

public sealed partial class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ContextItemKey = "ClearCase.CorrelationId";
    private const int MaxLength = 64;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetCorrelationId(context.Request.Headers[HeaderName].ToString());
        context.Items[ContextItemKey] = correlationId;
        System.Diagnostics.Activity.Current?.SetTag("clearcase.correlation_id", correlationId);
        context.Response.Headers[HeaderName] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await _next(context);
        }
    }

    [GeneratedRegex("[^A-Za-z0-9_-]")]
    private static partial Regex DisallowedCharacters();

    // Only the characters left after stripping reach logs, headers and traces, never the raw header value.
    private static string GetCorrelationId(string requestedId)
    {
        var cleaned = DisallowedCharacters().Replace(requestedId, string.Empty);
        return cleaned.Length is > 0 and <= MaxLength && cleaned.Length == requestedId.Length
            ? cleaned
            : Guid.NewGuid().ToString("N");
    }
}