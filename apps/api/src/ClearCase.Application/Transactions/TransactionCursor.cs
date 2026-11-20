using System.Globalization;
using System.Text;

namespace ClearCase.Application.Transactions;

public sealed record TransactionCursor(DateTimeOffset TransactionDate, Guid Id)
{
    private const string Version = "1";
    private const int MaxEncodedLength = 128;

    public string Encode() => Convert.ToBase64String(Encoding.UTF8.GetBytes(
            string.Join('|', Version, TransactionDate.UtcTicks.ToString(CultureInfo.InvariantCulture), Id.ToString("D"))))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryDecode(string? value, out TransactionCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(value) || value.Length > MaxEncodedLength)
            return false;
        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split('|');
            if (parts.Length != 3 || parts[0] != Version
                || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks
                || !Guid.TryParseExact(parts[2], "D", out var id))
                return false;
            cursor = new TransactionCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return false;
        }
    }
}
