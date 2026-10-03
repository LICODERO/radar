using System.Security.Cryptography;
using System.Text;

namespace Radar.Scanner;

public static class ContentHash
{
    /// <summary>12 hex chars of SHA-256 over the text with CRLF turned into LF and outer blanks trimmed; null for no text.</summary>
    public static string? Of(string? text)
    {
        if (text is null) return null;
        var normalized = text.Replace("\r\n", "\n").Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..12].ToLowerInvariant();
    }
}
