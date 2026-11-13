using ClearCase.Api.Disputes;
using ClearCase.Api.Middleware;
using ClearCase.Api.Transactions;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClearCase.Api.Endpoints;

public static class DisputeEndpoints
{
    public static WebApplication MapDisputeEndpoints(this WebApplication app)
    {
        foreach (var (prefix, suffix) in new[] { ("/api/v1", ""), ("/v1", "Alias") })
        {
            app.MapPost($"{prefix}/disputes", Create)
                .WithName("CreateDispute" + suffix)
                .WithSummary("Submit a dispute for an owned transaction")
                .WithDescription("Re-evaluates eligibility and opens a case. It does not reverse, refund or charge back funds.")
                .Accepts<CreateDisputeRequest>("application/json")
                .Produces<DisputeResponse>(StatusCodes.Status201Created)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
                .RequireAuthorization();

            app.MapGet($"{prefix}/disputes/{{id}}", Get)
                .WithName("GetDispute" + suffix)
                .WithSummary("Get an owned dispute and its timeline")
                .Produces<DisputeResponse>()
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .RequireAuthorization();
        }

        return app;
    }

    private static async Task<Results<Created<DisputeResponse>, ProblemHttpResult>> Create(
        CreateDisputeRequest request, HttpContext context, ISubmitDisputeHandler handler,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var errors = new List<QueryError>();
        if (!Guid.TryParse(request.TransactionId, out var transactionId))
            errors.Add(new("/transactionId", "transactionId.invalid", "Enter a valid transaction ID."));
        var reasonCode = request.ReasonCode?.Trim() ?? string.Empty;
        if (reasonCode.Length == 0)
            errors.Add(new("/reasonCode", "reasonCode.required", "Select a dispute reason."));
        var description = request.Description?.Trim() ?? string.Empty;
        if (description.Length is < Dispute.DescriptionMinLength or > Dispute.DescriptionMaxLength)
            errors.Add(new("/description", "description.length",
                $"Describe the problem in {Dispute.DescriptionMinLength} to {Dispute.DescriptionMaxLength} characters."));
        if (errors.Count != 0)
            return Problem(context, StatusCodes.Status400BadRequest, "Invalid dispute request",
                "dispute.invalidRequest", errors: errors);

        var result = await handler.HandleAsync(
            new SubmitDisputeCommand(transactionId, reasonCode, description), cancellationToken);
        return result switch
        {
            SubmitDisputeResult.Created created => TypedResults.Created(
                $"{context.Request.Path.Value!.TrimEnd('/')}/{created.Dispute.Id}",
                DisputeResponse.From(created.Dispute)),
            SubmitDisputeResult.TransactionNotFound => Problem(context,
                StatusCodes.Status422UnprocessableEntity, "Transaction not found", "dispute.transactionNotFound",
                errors: [new("/transactionId", "transactionId.notFound", "The transaction could not be found.")]),
            SubmitDisputeResult.InvalidReason => Problem(context,
                StatusCodes.Status422UnprocessableEntity, "Invalid dispute reason", "dispute.invalidReason",
                errors: [new("/reasonCode", "reasonCode.invalid", "Select a reason that applies to this transaction.")]),
            SubmitDisputeResult.Ineligible ineligible => Problem(context,
                StatusCodes.Status422UnprocessableEntity, "Transaction is not eligible for dispute",
                "dispute.transactionIneligible", ineligible.Eligibility.Explanation,
                extensions: new() { ["reasonCodes"] = ineligible.Eligibility.ReasonCodes }),
            SubmitDisputeResult.ActiveDisputeExists => Problem(context,
                StatusCodes.Status409Conflict, "An open dispute already exists", "dispute.activeCaseExists",
                "This transaction already has an open dispute."),
            _ => throw new InvalidOperationException("Unhandled dispute submission result.")
        };
    }

    private static async Task<Results<Ok<DisputeResponse>, ProblemHttpResult>> Get(
        string id, HttpContext context, IGetDisputeHandler handler, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(id, out var disputeId))
            return Problem(context, StatusCodes.Status400BadRequest, "Invalid dispute ID", "dispute.invalidId",
                errors: [new("/id", "disputeId.invalid", "Enter a valid dispute ID.")]);

        var dispute = await handler.HandleAsync(disputeId, cancellationToken);
        return dispute is null
            ? Problem(context, StatusCodes.Status404NotFound, "Dispute not found", "dispute.notFound")
            : TypedResults.Ok(DisputeResponse.From(dispute));
    }

    internal static ProblemHttpResult Problem(
        HttpContext context, int status, string title, string code, string? detail = null,
        IReadOnlyList<QueryError>? errors = null, Dictionary<string, object?>? extensions = null)
    {
        extensions ??= [];
        extensions["code"] = code;
        extensions["correlationId"] = context.Items[CorrelationIdMiddleware.ContextItemKey];
        if (errors is not null)
            extensions["errors"] = errors;
        return TypedResults.Problem(statusCode: status, title: title, detail: detail, extensions: extensions);
    }
}
