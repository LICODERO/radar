namespace Radar.Scanner;

public static class Visibilities
{
    /// <summary>Tracked by git: it is committed (or staged), so everyone who clones the repo gets it.</summary>
    public const string Public = "public";
    /// <summary>Not tracked and ignored (RADAR's block in .git/info/exclude, .gitignore...): stays on this machine.</summary>
    public const string Private = "private";
    /// <summary>Not tracked and not ignored: it shows up in git changes, so it is neither shared nor hidden yet.</summary>
    public const string Untracked = "untracked";
    /// <summary>Git could not tell (not installed, not a real repository, timeout).</summary>
    public const string Unknown = "unknown";
}

/// <summary>An AI file or folder whose visibility is asked: <paramref name="TrackedPath"/> is checked against the index (a skill is a folder), <paramref name="IgnorePath"/> against the ignore rules.</summary>
public sealed record VisibilityQuery(string Key, string TrackedPath, string IgnorePath);

/// <summary>Tells whether git shares an item (public), hides it (private) or has not decided yet (untracked). Two git calls per repo, only for the paths asked.</summary>
public static class GitVisibility
{
    public static IReadOnlyDictionary<string, string> Resolve(string repoDir, IReadOnlyCollection<VisibilityQuery> queries)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (queries.Count == 0) return result;

        var tracked = GitProcess.Run(repoDir, ["ls-files", "-z", "--", .. queries.Select(q => q.TrackedPath)]);
        if (tracked is not { ExitCode: 0 })
        {
            foreach (var q in queries) result[q.Key] = Visibilities.Unknown;
            return result;
        }
        var files = tracked.Text.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        var rest = queries.Where(q => !IsTracked(files, q.TrackedPath)).ToList();
        foreach (var q in queries.Except(rest)) result[q.Key] = Visibilities.Public;
        if (rest.Count == 0) return result;

        // check-ignore lists only the paths that are ignored (exit 1 = none)
        var ignored = GitProcess.Run(repoDir, ["check-ignore", "-z", "--stdin"], string.Join('\0', rest.Select(q => q.IgnorePath)) + '\0');
        if (ignored is null || ignored.ExitCode > 1)
        {
            foreach (var q in rest) result[q.Key] = Visibilities.Unknown;
            return result;
        }
        var hidden = ignored.Text.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        foreach (var q in rest) result[q.Key] = hidden.Contains(q.IgnorePath) ? Visibilities.Private : Visibilities.Untracked;
        return result;
    }

    private static bool IsTracked(string[] files, string path) =>
        files.Any(f => f == path || f.StartsWith(path + "/", StringComparison.Ordinal));
}
