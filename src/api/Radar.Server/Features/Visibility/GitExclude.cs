using System.Text;
using Radar.Scanner;
using Radar.Server.Features.Vault;

namespace Radar.Server.Features.Visibility;

/// <summary>
/// The private list: a block RADAR owns in the repository's <c>.git/info/exclude</c>. Unlike .gitignore the file is never committed,
/// so nobody sees what is kept private. Everything outside the markers is left alone.
/// </summary>
public static class GitExclude
{
    public const string Start = "# >>> radar:private >>>";
    public const string End = "# <<< radar:private <<<";
    private const string Note = "# Kept private by R.A.D.A.R. (edit through the app; lines between the markers are rewritten)";

    /// <summary>The ignore pattern for a repo-relative path (anchored to the repo root; a folder gets a trailing slash).</summary>
    public static string Entry(string relativePath, bool isDirectory)
    {
        var sb = new StringBuilder("/");
        foreach (var c in relativePath.Replace('\\', '/').Trim('/'))
        {
            if (c is '\\' or '*' or '?' or '[') sb.Append('\\');
            sb.Append(c);
        }
        if (sb[^1] == ' ') sb.Insert(sb.Length - 1, '\\');
        if (isDirectory) sb.Append('/');
        return sb.ToString();
    }

    /// <summary>The patterns inside RADAR's block, in file order (empty when there is no block).</summary>
    public static IReadOnlyList<string> Entries(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var s = text.IndexOf(Start, StringComparison.Ordinal);
        var e = text.IndexOf(End, StringComparison.Ordinal);
        if (s < 0 || e < s) return [];
        return text[(s + Start.Length)..e]
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();
    }

    /// <summary>Text with <paramref name="entry"/> in the block. Null when the markers are damaged (the caller must not write).</summary>
    public static string? Add(string? text, string entry) => With(text, Entries(text).Append(entry));

    /// <summary>Text without <paramref name="entry"/>; the block disappears with its last line. Null when the markers are damaged.</summary>
    public static string? Remove(string? text, string entry) => With(text, Entries(text).Where(e => e != entry));

    private static string? With(string? text, IEnumerable<string> entries)
    {
        var list = entries.Distinct(StringComparer.Ordinal).ToList();
        return list.Count == 0
            ? MarkedBlock.Remove(text, Start, End)
            : MarkedBlock.Upsert(text, Start, End, Note + "\n" + string.Join("\n", list));
    }

    /// <summary>The real <c>info/exclude</c> of the repository (worktrees and submodules point into the common git dir), or null when git cannot tell.</summary>
    public static string? ResolveFile(string repoDir)
    {
        var o = GitProcess.Run(repoDir, ["rev-parse", "--path-format=absolute", "--git-path", "info/exclude"]);
        if (o is not { ExitCode: 0 }) return null;
        var path = o.Text.Trim();
        return Path.IsPathRooted(path) ? path : null;
    }
}
