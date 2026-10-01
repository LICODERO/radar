namespace Radar.Server.Features.Vault;

/// <summary>A block of text between two marker lines that the app owns; everything outside the markers is left untouched.</summary>
public static class MarkedBlock
{
    /// <summary>
    /// Replaces the block between <paramref name="start"/> and <paramref name="end"/> (markers included) or appends it when absent.
    /// Returns null when the markers are damaged (only one of them, or in the wrong order), so the caller never guesses.
    /// </summary>
    public static string? Upsert(string? text, string start, string end, string inner)
    {
        var nl = text is not null && text.Contains("\r\n") ? "\r\n" : "\n";
        var block = start + nl + inner.Replace("\r\n", "\n").Trim('\n').Replace("\n", nl) + nl + end;
        if (string.IsNullOrEmpty(text)) return block + nl;

        var s = text.IndexOf(start, StringComparison.Ordinal);
        var e = text.IndexOf(end, StringComparison.Ordinal);
        if (s < 0 && e < 0) return text.TrimEnd('\r', '\n') + nl + nl + block + nl;
        if (s < 0 || e < 0 || e < s) return null;
        return text[..s] + block + text[(e + end.Length)..];
    }
}
