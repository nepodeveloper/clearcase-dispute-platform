using ClearCase.Api.Authentication;
using ClearCase.Api.Disputes;
using ClearCase.Api.Endpoints;
using ClearCase.Api.Middleware;
using ClearCase.Application.Disputes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace ClearCase.Api.Features.Disputes.SubmitDispute;

/// <summary>Customer command slice: validate and submit only; no external financial side effects.</summary>
public static class SubmitDisputeEndpoint
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string IdempotentReplayedHeader = "Idempotent-Replayed";

    public static void Map(IEndpointRouteBuilder app, string prefix, string suffix)
    {
        app.MapPost($"{prefix}/disputes", Create)
                .WithName("CreateDispute" + suffix)
                .WithSummary("Submit a dispute for an owned transaction")
                .WithDescription("Re-evaluates eligibility and opens a case. It does not reverse, refund or charge back funds. "
                    + "A valid Idempotency-Key is REQUIRED (1-128 visible ASCII characters) for every submission: retrying with the same key "
                    + "and payload returns the original case with Idempotent-Replayed: true; the same key with a different "
                    + "payload returns 409 dispute.idempotencyKeyReused.")
                .Accepts<CreateDisputeRequest>("application/json")
                .Produces<DisputeResponse>(StatusCodes.Status201Created)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
                .RequireAuthorization(AuthenticationPolicies.CustomerAccess)
                .RequireCsrfProtection();

    }

    private static async Task<Results<Created<DisputeResponse>, ProblemHttpResult>> Create(
        CreateDisputeRequest request, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        HttpContext context, ISubmitDisputeHandler handler, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var validation = SubmitDisputeRequestValidator.Validate(request, context.Request.Headers);
        if (validation.Errors.Count != 0)
            return ApiProblem.Create(context, StatusCodes.Status400BadRequest, "Invalid dispute request",
                "dispute.invalidRequest", errors: validation.Errors);

        var result = await handler.HandleAsync(
            new SubmitDisputeCommand(validation.TransactionId, validation.ReasonCode, validation.Description, idempotencyKey,
                (string)context.Items[CorrelationIdMiddleware.ContextItemKey]!), cancellationToken);
        if (result is SubmitDisputeResult.Replayed)
            context.Response.Headers[IdempotentReplayedHeader] = "true";
        return result switch
        {
            SubmitDisputeResult.Created created => TypedResults.Created(
                $"{context.Request.Path.Value!.TrimEnd('/')}/{created.Dispute.Id}",
                DisputeResponse.From(created.Dispute)),
            SubmitDisputeResult.Replayed replayed => TypedResults.Created(
                $"{context.Request.Path.Value!.TrimEnd('/')}/{replayed.Dispute.Id}",
                DisputeResponse.From(replayed.Dispute)),
            SubmitDisputeResult.IdempotencyKeyReused => ApiProblem.Create(context,
                StatusCodes.Status409Conflict, "Idempotency key already used", "dispute.idempotencyKeyReused",
                "This Idempotency-Key was already used for a different dispute request. Use a new key for a new request."),
            SubmitDisputeResult.TransactionNotFound => ApiProblem.Create(context,
                StatusCodes.Status422UnprocessableEntity, "Transaction not found", "dispute.transactionNotFound",
                errors: [new("/transactionId", "transactionId.notFound", "The transaction could not be found.")]),
            SubmitDisputeResult.InvalidReason => ApiProblem.Create(context,
                StatusCodes.Status422UnprocessableEntity, "Invalid dispute reason", "dispute.invalidReason",
                errors: [new("/reasonCode", "reasonCode.invalid", "Select a reason that applies to this transaction.")]),
            SubmitDisputeResult.Ineligible ineligible => ApiProblem.Create(context,
                StatusCodes.Status422UnprocessableEntity, "Transaction is not eligible for dispute",
                "dispute.transactionIneligible", ineligible.Eligibility.Explanation,
                extensions: new() { ["reasonCodes"] = ineligible.Eligibility.ReasonCodes }),
            SubmitDisputeResult.ActiveDisputeExists => ApiProblem.Create(context,
                StatusCodes.Status409Conflict, "An open dispute already exists", "dispute.activeCaseExists",
                "This transaction already has an open dispute."),
            _ => throw new InvalidOperationException("Unhandled dispute submission result.")
        };
    }

}
