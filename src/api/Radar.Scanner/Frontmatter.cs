namespace Radar.Scanner;

/// <summary>Minimal, tolerant reader for the YAML-ish frontmatter used by Claude Code files.</summary>
public sealed class Frontmatter
{
    private readonly Dictionary<string, string> _scalars = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _lists = new(StringComparer.OrdinalIgnoreCase);

    public bool Present { get; private init; }
    public bool Valid { get; private init; } = true;

    public string? Get(string key) => _scalars.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

    /// <summary>List value: inline `[a, b]`, block `- a`, or a comma separated scalar.</summary>
    public IReadOnlyList<string> GetList(string key)
    {
        if (_lists.TryGetValue(key, out var l)) return l;
        var s = Get(key);
        return s is null ? [] : SplitList(s);
    }

    public static Frontmatter Parse(string? text)
    {
        var fm = new Frontmatter { Present = false };
        if (string.IsNullOrEmpty(text)) return fm;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---") return fm;

        var end = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---") { end = i; break; }
        }
        if (end < 0) return new Frontmatter { Present = true, Valid = false };

        var result = new Frontmatter { Present = true };
        string? listKey = null;
        for (var i = 1; i < end; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;

            var trimmed = line.TrimStart();
            if (listKey is not null && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                result._lists[listKey].Add(Unquote(trimmed[2..].Trim()));
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0 || char.IsWhiteSpace(line[0])) { listKey = null; continue; }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (value.Length == 0)
            {
                listKey = key;
                result._lists[key] = [];
                continue;
            }
            listKey = null;
            result._scalars[key] = Unquote(value);
        }
        return result;
    }

    /// <summary>The text after the frontmatter block (the whole text when there is none or it is not closed).</summary>
    public static string BodyOf(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---") return string.Join('\n', lines);
        for (var i = 1; i < lines.Length; i++)
            if (lines[i].Trim() == "---") return string.Join('\n', lines.Skip(i + 1));
        return string.Join('\n', lines);
    }

    private static IReadOnlyList<string> SplitList(string s)
    {
        var t = s.Trim();
        if (t.StartsWith('[') && t.EndsWith(']')) t = t[1..^1];
        return t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Unquote).Where(x => x.Length > 0).ToList();
    }

    private static string Unquote(string v)
    {
        if (v.Length >= 2 && ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\''))) return v[1..^1];
        return v;
    }
}
