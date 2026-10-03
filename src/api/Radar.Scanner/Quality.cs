using System.Text.RegularExpressions;

namespace Radar.Scanner;

/// <summary>
/// Heuristic quality checks of the AI files a repo already has. Coverage says that a file exists; this says whether it is worth anything.
/// Pure and deterministic: text in, findings out (only existence of referenced paths is checked on disk, inside the repo).
/// </summary>
public static partial class Quality
{
    /// <summary>A repo with a file scoring below this gets the <see cref="GapTypes.WeakFiles"/> gap (one broken file is enough; the repo score is an average and would hide it).</summary>
    public const int WeakThreshold = 70;

    public const int ClaudeMdMaxLines = 300;
    public const int MinDescription = 30;
    public const int MinSkillDescription = 40;
    public const int MinBodyLines = 5;

    /// <summary>CLAUDE.md untouched this long while the repo kept moving is probably out of date.</summary>
    public const int StaleDays = 180;

    private const int MaxRefsReported = 3;

    public static int PenaltyOf(string severity) => severity switch
    {
        Severities.Error => 35,
        Severities.Warning => 15,
        _ => 5
    };

    public static int ScoreOf(IEnumerable<QualityFinding> findings) =>
        Math.Max(0, 100 - findings.Sum(f => PenaltyOf(f.Severity)));

    /// <summary>Whole-repo result: per-file scores and the repo score (average of the existing files).</summary>
    public static QualityInfo Summarize(IReadOnlyList<(string Path, string Kind)> files, IReadOnlyList<QualityFinding> findings)
    {
        if (files.Count == 0) return new QualityInfo(null, [], findings);
        var perFile = files
            .Select(f => new FileQuality(f.Path, f.Kind, ScoreOf(findings.Where(x => x.Path == f.Path))))
            .ToList();
        return new QualityInfo((int)Math.Round(perFile.Average(f => f.Score)), perFile, findings);
    }

    public static bool IsWeak(QualityInfo q) => q.Files.Any(f => f.Score < WeakThreshold);

    // ---- CLAUDE.md --------------------------------------------------------------------------------

    public static IReadOnlyList<QualityFinding> ClaudeMd(string repoDir, string path, string? text)
    {
        var f = new List<QualityFinding>();
        if (text is null) return f;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var nonEmpty = lines.Count(l => !string.IsNullOrWhiteSpace(l));

        if (nonEmpty < 4) f.Add(new(path, "claude-md-thin", Severities.Error, nonEmpty.ToString()));
        else if (nonEmpty < 12) f.Add(new(path, "claude-md-thin", Severities.Warning, nonEmpty.ToString()));

        if (lines.Length > ClaudeMdMaxLines) f.Add(new(path, "claude-md-too-long", Severities.Warning, lines.Length.ToString()));

        var (outsideFences, fenceCount) = SplitFences(lines);
        if (nonEmpty >= 4 && fenceCount == 0 && !InlineCommand().IsMatch(outsideFences))
            f.Add(new(path, "claude-md-no-commands", Severities.Warning));

        if (nonEmpty >= 12 && !Heading().IsMatch(outsideFences))
            f.Add(new(path, "claude-md-no-structure", Severities.Info));

        if (Placeholder().IsMatch(text)) f.Add(new(path, "claude-md-placeholder", Severities.Warning));

        var broken = BrokenReferences(repoDir, outsideFences);
        if (broken.Count > 0) f.Add(new(path, "claude-md-broken-ref", Severities.Warning, string.Join(", ", broken.Take(MaxRefsReported))));
        return f;
    }

    /// <summary>Text outside ``` fences (joined) and how many fenced blocks there are; references inside fences are examples, not links.</summary>
    private static (string Outside, int Fences) SplitFences(string[] lines)
    {
        var outside = new List<string>();
        var inFence = false;
        var fences = 0;
        foreach (var l in lines)
        {
            if (l.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (!inFence) fences++;
                inFence = !inFence;
                continue;
            }
            if (!inFence) outside.Add(l);
        }
        return (string.Join('\n', outside), fences);
    }

    private static List<string> BrokenReferences(string repoDir, string text)
    {
        var candidates = new List<string>();
        foreach (Match m in AtImport().Matches(text)) candidates.Add(m.Groups[1].Value);
        foreach (Match m in MdLink().Matches(text)) candidates.Add(m.Groups[1].Value);
        foreach (Match m in ClaudePathSpan().Matches(text)) candidates.Add(m.Groups[1].Value);

        var broken = new List<string>();
        foreach (var raw in candidates.Distinct(StringComparer.Ordinal))
        {
            var rel = raw.Split('#')[0].Trim().TrimEnd('.', ',', ';', ':', ')');
            if (rel.Length == 0 || rel.StartsWith('/') || rel.StartsWith('~') || rel.Contains("://") || rel.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || rel.IndexOfAny(['*', '{', '}', '<', '>', '$', '?']) >= 0) continue;
            var full = Path.GetFullPath(Path.Combine(repoDir, rel.Replace('/', Path.DirectorySeparatorChar)));
            // a path leaving the repo is not ours to judge; only a path inside it that does not exist is broken
            if (!SafeFs.IsInside(repoDir, full)) continue;
            if (!File.Exists(full) && !Directory.Exists(full)) broken.Add(rel);
        }
        return broken;
    }

    /// <summary>
    /// <paramref name="fileTime"/> is when CLAUDE.md last changed on disk, <paramref name="repoActivity"/> when the repo last moved (last git write);
    /// null when unknown. A fresh clone stamps both alike, so this errs on the side of saying nothing.
    /// </summary>
    public static QualityFinding? Stale(string path, DateTime? fileTime, DateTime? repoActivity)
    {
        if (fileTime is null || repoActivity is null) return null;
        var days = (int)(repoActivity.Value - fileTime.Value).TotalDays;
        return days >= StaleDays ? new QualityFinding(path, "claude-md-stale", Severities.Warning, days.ToString()) : null;
    }

    /// <summary>Last write of the git reflog: moves on every commit, checkout and pull. Null when the repo has no readable one (worktrees, submodules).</summary>
    public static DateTime? RepoActivity(string repoDir)
    {
        try
        {
            var log = Path.Combine(repoDir, ".git", "logs", "HEAD");
            return File.Exists(log) ? File.GetLastWriteTimeUtc(log) : null;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return null; }
    }

    // ---- agents and skills ------------------------------------------------------------------------

    public static IReadOnlyList<QualityFinding> Agent(string path, string fileName, string? text, Frontmatter fm)
    {
        var f = new List<QualityFinding>();
        if (text is null) return f;
        if (!fm.Present || !fm.Valid)
        {
            f.Add(new(path, "agent-no-frontmatter", Severities.Error));
        }
        else
        {
            if (fm.Get("name") is null) f.Add(new(path, "agent-no-name", Severities.Error));
            else if (!string.Equals(fm.Get("name"), fileName, StringComparison.OrdinalIgnoreCase)) f.Add(new(path, "agent-name-mismatch", Severities.Info, fm.Get("name")));
            var d = fm.Get("description");
            if (d is null) f.Add(new(path, "agent-no-description", Severities.Error));
            else if (d.Length < MinDescription) f.Add(new(path, "agent-short-description", Severities.Warning, d.Length.ToString()));
            if (fm.Get("tools") is null && fm.GetList("tools").Count == 0) f.Add(new(path, "agent-no-tools", Severities.Info));
        }
        if (BodyLines(text, fm) < MinBodyLines) f.Add(new(path, "agent-thin-prompt", Severities.Warning));
        return f;
    }

    public static IReadOnlyList<QualityFinding> Skill(string path, string dirName, string? text, Frontmatter fm)
    {
        var f = new List<QualityFinding>();
        if (text is null) return f;
        if (!fm.Present || !fm.Valid)
        {
            f.Add(new(path, "skill-no-frontmatter", Severities.Error));
        }
        else
        {
            if (fm.Get("name") is { } n && !string.Equals(n, dirName, StringComparison.OrdinalIgnoreCase)) f.Add(new(path, "skill-name-mismatch", Severities.Info, n));
            var d = fm.Get("description");
            if (d is null) f.Add(new(path, "skill-no-description", Severities.Error));
            else if (d.Length < MinSkillDescription) f.Add(new(path, "skill-short-description", Severities.Warning, d.Length.ToString()));
        }
        if (BodyLines(text, fm) < MinBodyLines) f.Add(new(path, "skill-thin-body", Severities.Warning));
        return f;
    }

    /// <summary>Agents sharing a name: Claude Code keeps one of them, the other is dead weight.</summary>
    public static IReadOnlyList<QualityFinding> DuplicateAgents(IReadOnlyList<AgentInfo> agents) =>
        agents.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Skip(1).Select(a => new QualityFinding(a.Path, "agent-duplicate-name", Severities.Warning, a.Name)))
            .ToList();

    private static int BodyLines(string text, Frontmatter fm) =>
        (fm.Present && fm.Valid ? Frontmatter.BodyOf(text) : text)
            .Split('\n').Count(l => !string.IsNullOrWhiteSpace(l));

    // ---- patterns ---------------------------------------------------------------------------------

    /// <summary>`something with a space` (a command) in a CLAUDE.md that has no fenced block.</summary>
    [GeneratedRegex(@"`[^`\n]*\s[^`\n]*`")]
    private static partial Regex InlineCommand();

    [GeneratedRegex(@"^#{1,6}\s+\S", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"lorem ipsum|<\s*project name\s*>|\[\s*project name\s*\]|describe (the )?project here|your project here|\bTBD\b|\bFIXME\b|<fill|\[fill in", RegexOptions.IgnoreCase)]
    private static partial Regex Placeholder();

    /// <summary>`@docs/guide.md` imports: after whitespace or line start, must end in a file extension, so @mentions, emails and npm scopes like @angular/core do not match.</summary>
    [GeneratedRegex(@"(?:^|\s)@((?:[\w.-]+/)*[\w-]+\.\w{1,5})(?=[\s,;:)]|\.(?:\s|$)|$)", RegexOptions.Multiline)]
    private static partial Regex AtImport();

    [GeneratedRegex(@"\]\(([^)\s]+)\)")]
    private static partial Regex MdLink();

    /// <summary>`.claude/...` paths named in backticks.</summary>
    [GeneratedRegex(@"`(\.claude/[^`\s]+)`")]
    private static partial Regex ClaudePathSpan();
}
