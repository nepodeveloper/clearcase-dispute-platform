using System.Text;

namespace ClearCase.Domain.Evidence;

public sealed record CustomerEvidenceFileType(string ContentType);

/// <summary>Rules for customer-supplied dispute evidence; the declared content type is never trusted.</summary>
public static class CustomerEvidenceRules
{
    public const int MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxFilesPerDispute = 5;
    public const int NoteMaxLength = 200;
    public const int FileNameMaxLength = 100;
    public const string FallbackFileName = "evidence";

    private static readonly CustomerEvidenceFileType Pdf = new("application/pdf");
    private static readonly CustomerEvidenceFileType Png = new("image/png");
    private static readonly CustomerEvidenceFileType Jpeg = new("image/jpeg");

    /// <summary>Identifies PDF, PNG and JPEG by their leading signature bytes; anything else is unsupported.</summary>
    public static CustomerEvidenceFileType? Sniff(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith("%PDF-"u8)) return Pdf;
        if (content.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return Png;
        if (content.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return Jpeg;
        return null;
    }

    /// <summary>Display-only name: no directory part, no control or reserved characters, bounded length.</summary>
    public static string SanitizeFileName(string? original)
    {
        var name = original ?? string.Empty;
        var separator = name.LastIndexOfAny(['/', '\\']);
        if (separator >= 0) name = name[(separator + 1)..];

        var cleaned = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (char.IsControl(character) || character is '<' or '>' or ':' or '"' or '|' or '?' or '*')
                continue;
            cleaned.Append(character);
        }

        var result = cleaned.ToString().Trim().Trim('.');
        if (result.Length > FileNameMaxLength)
        {
            var length = char.IsHighSurrogate(result[FileNameMaxLength - 1]) ? FileNameMaxLength - 1 : FileNameMaxLength;
            result = result[..length].TrimEnd();
        }
        return result.Length == 0 ? FallbackFileName : result;
    }
}
