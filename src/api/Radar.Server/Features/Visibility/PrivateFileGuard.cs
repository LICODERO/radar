using Radar.Scanner;

namespace Radar.Server.Features.Visibility;

/// <param name="Blocked">null, or why the file cannot be kept out of git: no-git | tracked | exclude-damaged</param>
/// <param name="ExcludeEntry">the line RADAR would add to its private block, or null when git already ignores the file</param>
public sealed record GuardResult(string? Blocked, string? ExcludeEntry);

/// <summary>
/// Makes sure a file RADAR is about to write for the user alone (CLAUDE.local.md, .claude/settings.local.json) never reaches a commit:
/// git already ignores it, or its path goes into RADAR's private block of .git/info/exclude. Refuses when git cannot tell or when the
/// file is already tracked (private content in a tracked file would be committed).
/// </summary>
public static class PrivateFileGuard
{
    public static GuardResult Check(string repoDir, string relativePath)
    {
        var state = GitVisibility.Resolve(repoDir, [new VisibilityQuery("f", relativePath, relativePath)])["f"];
        if (state == Visibilities.Unknown) return new GuardResult("no-git", null);
        if (state == Visibilities.Public) return new GuardResult("tracked", null);
        if (state == Visibilities.Private) return new GuardResult(null, null);
        var why = ItemVisibility.Hide(repoDir, relativePath, false, apply: false);
        return why is not null ? new GuardResult(why, null) : new GuardResult(null, GitExclude.Entry(relativePath, false));
    }

    /// <summary>Puts the path on the private list (call after a successful <see cref="Check"/> that returned an entry).</summary>
    public static void Apply(string repoDir, string relativePath)
    {
        if (ItemVisibility.Hide(repoDir, relativePath, false, apply: true) is not null) throw new IOException("cannot hide " + relativePath);
    }

    /// <summary>The path of the exclude file, to restore it when a later step fails.</summary>
    public static string? ExcludeFile(string repoDir) => GitExclude.ResolveFile(repoDir);
}
