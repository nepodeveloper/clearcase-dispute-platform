using System.Globalization;
using System.Security.Claims;
using ClearCase.Api.Middleware;
using ClearCase.Api.Transactions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace ClearCase.Api.Endpoints;

public static class TransactionEndpoints
{
    public static WebApplication MapTransactionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/transactions", List)
            .WithName("ListTransactions")
            .WithSummary("List the authenticated customer's synthetic transactions")
            .Produces<TransactionListResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        app.MapGet("/v1/transactions", List)
            .WithName("ListTransactionsAlias")
            .WithSummary("Compatibility alias for the transaction list")
            .Produces<TransactionListResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();
        return app;
    }

    private static async Task<Results<Ok<TransactionListResponse>, ProblemHttpResult>> List(
        [AsParameters] TransactionListRequest request, HttpContext context,
        ITransactionReader reader, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var errors = new List<QueryError>();
        var pageNumber = ParseNumber(request.PageNumber, 1, int.MaxValue, "pageNumber", errors);
        var pageSize = ParseNumber(request.PageSize, 20, 100, "pageSize", errors);
        var fromDate = ParseDate(request.FromDate, "fromDate", errors);
        var toDate = ParseDate(request.ToDate, "toDate", errors);
        TransactionType? type = null;
        if (request.TransactionType is not null)
        {
            if (!Enum.TryParse<TransactionType>(request.TransactionType, false, out var parsed)
                || !Enum.GetNames<TransactionType>().Contains(request.TransactionType))
                errors.Add(new("/transactionType", "transactionType.invalid", "Select a supported transaction type."));
            else
                type = parsed;
        }
        if (fromDate > toDate)
            errors.Add(new("/toDate", "date.invalidRange", "toDate must not precede fromDate."));
        if (errors.Count != 0)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid transaction query",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "transaction.invalidQuery",
                    ["correlationId"] = context.Items[CorrelationIdMiddleware.ContextItemKey],
                    ["errors"] = errors
                });
        }

        var customerClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(customerClaim, out var customerId))
            throw new InvalidOperationException("The authenticated principal has no valid customer identity.");

        var result = await reader.ReadAsync(customerId, new(pageNumber, pageSize, type, fromDate, toDate), cancellationToken);
        return TypedResults.Ok(result);
    }

    private static int ParseNumber(string? value, int defaultValue, int maximum, string name, List<QueryError> errors)
    {
        if (value is null) return defaultValue;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
            && result >= 1 && result <= maximum) return result;
        errors.Add(new($"/{name}", $"{name}.invalid", $"{name} must be between 1 and {maximum}."));
        return defaultValue;
    }

    private static DateOnly? ParseDate(string? value, string name, List<QueryError> errors)
    {
        if (value is null) return null;
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        errors.Add(new($"/{name}", $"{name}.invalid", $"{name} must be a date in YYYY-MM-DD format."));
        return null;
    }
}
