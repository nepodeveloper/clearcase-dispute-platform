using System.Globalization;
using ClearCase.Application.DisputeEligibility;
using ClearCase.Application.Transactions;
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

        app.MapGet("/api/v1/transactions/{id}", GetDetails)
            .WithName("GetTransactionDetails")
            .WithSummary("Get an authenticated customer's synthetic transaction")
            .WithDescription("Returns transaction details only when the authenticated customer owns the transaction.")
            .Produces<TransactionDetailsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        app.MapGet("/v1/transactions/{id}", GetDetails)
            .WithName("GetTransactionDetailsAlias")
            .WithSummary("Compatibility alias for transaction details")
            .WithDescription("Returns transaction details only when the authenticated customer owns the transaction.")
            .Produces<TransactionDetailsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        app.MapGet("/api/v1/transactions/{id}/dispute-eligibility", GetDisputeEligibility)
            .WithName("GetTransactionDisputeEligibility")
            .WithSummary("Evaluate dispute eligibility for an owned transaction")
            .WithDescription("Returns a read-only eligibility policy decision. It does not submit a dispute or initiate a financial operation.")
            .Produces<TransactionDisputeEligibilityResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .RequireAuthorization();

        app.MapGet("/v1/transactions/{id}/dispute-eligibility", GetDisputeEligibility)
            .WithName("GetTransactionDisputeEligibilityAlias")
            .WithSummary("Compatibility alias for transaction dispute eligibility")
            .Produces<TransactionDisputeEligibilityResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .RequireAuthorization();
        return app;
    }

    private static async Task<Results<Ok<TransactionDisputeEligibilityResponse>, ProblemHttpResult>>
        GetDisputeEligibility(
            string id,
            HttpContext context,
            IGetDisputeEligibilityHandler handler,
            CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            return Task.CompletedTask;
        });
        if (!Guid.TryParse(id, out var transactionId))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid transaction ID",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "transaction.invalidId",
                    ["correlationId"] = context.Items[CorrelationIdMiddleware.ContextItemKey],
                    ["errors"] = new[] { new QueryError("/id", "transactionId.invalid", "Enter a valid transaction ID.") }
                });
        }

        var result = await handler.HandleAsync(transactionId, cancellationToken);
        if (result is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound,
                title: "Transaction not found",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "transaction.notFound",
                    ["correlationId"] = context.Items[CorrelationIdMiddleware.ContextItemKey]
                });
        }

        return TypedResults.Ok(TransactionDisputeEligibilityResponse.From(transactionId, result));
    }

    private static async Task<Results<Ok<TransactionDetailsResponse>, ProblemHttpResult>> GetDetails(
        string id, HttpContext context, IGetTransactionDetailsHandler handler, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            return Task.CompletedTask;
        });
        if (!Guid.TryParse(id, out var transactionId))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid transaction ID",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "transaction.invalidId",
                    ["correlationId"] = context.Items[CorrelationIdMiddleware.ContextItemKey],
                    ["errors"] = new[] { new QueryError("/id", "transactionId.invalid", "Enter a valid transaction ID.") }
                });
        }

        var transaction = await handler.HandleAsync(transactionId, cancellationToken);
        if (transaction is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound,
                title: "Transaction not found",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "transaction.notFound",
                    ["correlationId"] = context.Items[CorrelationIdMiddleware.ContextItemKey]
                });
        }

        return TypedResults.Ok(new TransactionDetailsResponse(
            transaction.Id, transaction.TransactionType, transaction.Description, transaction.MerchantName,
            transaction.Amount.ToString("0.00", CultureInfo.InvariantCulture), transaction.Currency,
            transaction.TransactionDate, transaction.Status,
            transaction.Reference, transaction.IsDisputable));
    }

    private static async Task<Results<Ok<TransactionListResponse>, ProblemHttpResult>> List(
        [AsParameters] TransactionListRequest request, HttpContext context,
        IListTransactionsHandler handler, CancellationToken cancellationToken)
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

        var result = await handler.HandleAsync(
            new(pageNumber, pageSize, type, fromDate, toDate), cancellationToken);
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
