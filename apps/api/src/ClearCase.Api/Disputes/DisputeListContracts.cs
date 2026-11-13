using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using ClearCase.Application.Disputes;
using Microsoft.AspNetCore.WebUtilities;

namespace ClearCase.Api.Disputes;

/// <summary>A bounded customer-owned page, ordered newest first then by case reference.</summary>
public sealed record DisputeListResponse(IReadOnlyList<DisputeSummaryResponse> Items, DisputePageInfo PageInfo);

/// <summary>Continuation information; the cursor is opaque to clients and is not an authorization token.</summary>
public sealed record DisputePageInfo(bool HasNextPage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor);

/// <summary>Customer-safe case summary, without complaint text, evidence, audit data or full history.</summary>
public sealed record DisputeSummaryResponse(Guid Id, string Reference, Guid TransactionId, string Category,
    string CaseStatus, string DecisionStatus, string FinancialStatus, DateTimeOffset CreatedAt, string StatusDescription)
{
    public static DisputeSummaryResponse From(DisputeListItem item) =>
        new(item.Id, item.Reference, item.TransactionId, item.Category.ToString(), item.CaseStatus.ToString(),
            item.DecisionStatus.ToString(), item.FinancialStatus.ToString(), item.CreatedAt,
            DisputeResponse.DescribeStatus(item.CaseStatus, item.DecisionStatus, item.FinancialStatus));
}

internal static class DisputeCursorCodec
{
    public static string Encode(DisputeListItem item) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
        $"1|{item.CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{item.Reference}"));

    public static bool TryDecode(string value, out DisputeCursor? cursor)
    {
        cursor = null;
        if (value.Length is 0 or > 160) return false;
        try
        {
            var parts = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(value)).Split('|');
            if (parts.Length != 3 || parts[0] != "1"
                || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks
                || parts[2].Length is 0 or > 64
                || !parts[2].All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
                return false;
            cursor = new(new DateTimeOffset(ticks, TimeSpan.Zero), parts[2]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
