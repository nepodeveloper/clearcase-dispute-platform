using ClearCase.Api.Disputes;
using ClearCase.Api.Transactions;
using ClearCase.Domain.Disputes;
using Microsoft.AspNetCore.Http;

namespace ClearCase.Api.Features.Disputes.SubmitDispute;

/// <summary>Pure request/header validation; the domain still owns business eligibility.</summary>
internal sealed record SubmitDisputeValidation(
    Guid TransactionId, string ReasonCode, string Description, IReadOnlyList<QueryError> Errors);

internal static class SubmitDisputeRequestValidator
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const int IdempotencyKeyMaxLength = 128;

    internal static SubmitDisputeValidation Validate(CreateDisputeRequest request, IHeaderDictionary headers)
    {
        var errors = new List<QueryError>();
        if (!headers.TryGetValue(IdempotencyKeyHeader, out var keyValues)
            || keyValues.Count == 0)
            errors.Add(new("/headers/" + IdempotencyKeyHeader, "idempotencyKey.required",
                "Send an Idempotency-Key header to safely retry this submission."));
        else if (keyValues.Count != 1 || !IsValidIdempotencyKey(keyValues[0]))
            errors.Add(new("/headers/" + IdempotencyKeyHeader, "idempotencyKey.invalid",
                $"Send exactly one Idempotency-Key of 1 to {IdempotencyKeyMaxLength} visible ASCII characters."));
        if (!Guid.TryParse(request.TransactionId, out var transactionId))
            errors.Add(new("/transactionId", "transactionId.invalid", "Enter a valid transaction ID."));
        var reasonCode = request.ReasonCode?.Trim() ?? string.Empty;
        if (reasonCode.Length == 0)
            errors.Add(new("/reasonCode", "reasonCode.required", "Select a dispute reason."));
        var description = request.Description?.Trim() ?? string.Empty;
        if (description.Length is < Dispute.DescriptionMinLength or > Dispute.DescriptionMaxLength)
            errors.Add(new("/description", "description.length",
                $"Describe the problem in {Dispute.DescriptionMinLength} to {Dispute.DescriptionMaxLength} characters."));
        return new SubmitDisputeValidation(transactionId, reasonCode, description, errors);
    }

    private static bool IsValidIdempotencyKey(string? key) =>
        key is { Length: > 0 and <= IdempotencyKeyMaxLength } && key.All(character => character is > ' ' and <= '~');
}
