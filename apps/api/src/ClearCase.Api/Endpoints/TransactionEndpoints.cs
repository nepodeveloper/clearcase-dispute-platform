using System.Globalization;
using ClearCase.Api.Features.Transactions.GetDisputeEligibility;
using ClearCase.Application.Transactions;
using ClearCase.Api.Authentication;
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
            .WithDescription("Search description, merchant or reference with search (trimmed, at most 100 characters). "
                + "Filtering precedes pagination. Page with pageNumber or, preferably, the opaque cursor from "
                + "pageInfo.nextCursor (never both; cursor.conflict and cursor.invalid return 400). "
                + "activeDisputeId is present only for an owned case "
                + "that is not closed. isDisputable is an affordance; the eligibility endpoint remains authoritative.")
            .Produces<TransactionListResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization(AuthenticationPolicies.CustomerAccess);

        app.MapGet("/v1/transactions", List)
            .WithName("ListTransactionsAlias")
            .WithSummary("Compatibility alias for the transaction list")
            .Produces<TransactionListResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization(AuthenticationPolicies.CustomerAccess);

        app.MapGet("/api/v1/transactions/{id}", GetDetails)
            .WithName("GetTransactionDetails")
            .WithSummary("Get an authenticated customer's synthetic transaction")
            .WithDescription("Returns transaction details only when the authenticated customer owns the transaction.")
            .Produces<TransactionDetailsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthenticationPolicies.CustomerAccess);

        app.MapGet("/v1/transactions/{id}", GetDetails)
            .WithName("GetTransactionDetailsAlias")
            .WithSummary("Compatibility alias for transaction details")
            .WithDescription("Returns transaction details only when the authenticated customer owns the transaction.")
            .Produces<TransactionDetailsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthenticationPolicies.CustomerAccess);

        GetDisputeEligibilityEndpoint.Map(app);
        return app;
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
            return ApiProblem.Create(context, StatusCodes.Status400BadRequest,
                "Invalid transaction ID", "transaction.invalidId",
                errors: [new QueryError("/id", "transactionId.invalid", "Enter a valid transaction ID.")]);
        }

        var transaction = await handler.HandleAsync(transactionId, cancellationToken);
        if (transaction is null)
        {
            return ApiProblem.Create(context, StatusCodes.Status404NotFound,
                "Transaction not found", "transaction.notFound");
        }

        return TypedResults.Ok(new TransactionDetailsResponse(
            transaction.Id, transaction.TransactionType, transaction.Description, transaction.MerchantName,
            transaction.Amount.ToString("0.00", CultureInfo.InvariantCulture), transaction.Currency,
            transaction.TransactionDate, transaction.Status,
            transaction.Reference, transaction.IsDisputable, transaction.ActiveDisputeId));
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
        var search = request.Search?.Trim();
        if (search?.Length > 100)
            errors.Add(new("/search", "search.tooLong", "Search must be at most 100 characters."));
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
        TransactionCursor? after = null;
        if (request.Cursor is not null)
        {
            if (request.PageNumber is not null)
                errors.Add(new("/cursor", "cursor.conflict", "Use either cursor or pageNumber, not both."));
            else if (!TransactionCursor.TryDecode(request.Cursor, out after))
                errors.Add(new("/cursor", "cursor.invalid",
                    "Use a continuation cursor returned by this endpoint in pageInfo.nextCursor."));
        }
        if (errors.Count != 0)
        {
            return ApiProblem.Create(context, StatusCodes.Status400BadRequest,
                "Invalid transaction query", "transaction.invalidQuery", errors: errors);
        }

        var result = await handler.HandleAsync(
            new(pageNumber, pageSize, type, fromDate, toDate, string.IsNullOrEmpty(search) ? null : search, after),
            cancellationToken);
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
