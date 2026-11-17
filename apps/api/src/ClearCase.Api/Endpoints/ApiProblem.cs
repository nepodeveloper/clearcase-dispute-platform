using ClearCase.Api.Middleware;
using ClearCase.Api.Transactions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClearCase.Api.Endpoints;

/// <summary>One RFC 9457-style error contract for API command and query endpoints.</summary>
public static class ApiProblem
{
    public static ProblemHttpResult Create(
        HttpContext context, int status, string title, string code, string? detail = null,
        IReadOnlyList<QueryError>? errors = null, Dictionary<string, object?>? extensions = null)
    {
        var properties = extensions is null ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(extensions);
        properties["code"] = code;
        properties["correlationId"] = context.Items.TryGetValue(
            CorrelationIdMiddleware.ContextItemKey, out var correlationId)
            ? correlationId : context.TraceIdentifier;
        if (errors is not null)
            properties["errors"] = errors;
        return TypedResults.Problem(
            statusCode: status, title: title, detail: detail, extensions: properties);
    }
}
