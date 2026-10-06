using System.Text;
using Radar.Scanner;
using Radar.Server.Features.Files;
using Radar.Server.Features.Gaps;

namespace Radar.Server.Features.Visibility;

/// <param name="Kind">exclude-add | exclude-remove | git-rm-cached</param>
/// <param name="Text">what happens: the line for the exclude file, or the git command</param>
public sealed record VisibilityChange(string Kind, string Text);

/// <param name="Current">the state git reports now (not the one the scan saw)</param>
/// <param name="Blocked">null, or why nothing can be done: no-git | exclude-damaged | other-rule | unknown-state</param>
/// <param name="Notes">codes the UI turns into advice: commit-removal (the file leaves the repo for everyone after the commit), commit-to-share</param>
/// <param name="Applied">false for a plan; true once the changes were written</param>
/// <param name="Visibility">the state after the change (or the unchanged one for a plan)</param>
public sealed record VisibilityOutcome(
    string Path, string Current, string Target, IReadOnlyList<VisibilityChange> Changes,
    string? Blocked, IReadOnlyList<string> Notes, bool Applied, string Visibility);

public sealed class VisibilityFailure(Infrastructure.Localization.Msg key, params object[] args) : Exception
{
    public Infrastructure.Localization.Msg Key { get; } = key;
    public object[] Args { get; } = args;
}

/// <summary>
/// Makes an agent, skill or workflow private (listed in RADAR's block of .git/info/exclude, untracked first when git already tracks it)
/// or public again (removed from the block). The path must be one the scan reported; nothing outside the repo is touched.
/// </summary>
public static class ItemVisibility
{
    private sealed record Item(string Path, string TrackedPath, string IgnorePath, bool IsDirectory);

    /// <summary>The item behind a scan path (a skill's SKILL.md stands for its folder), or null when the scan did not report it.</summary>
    private static Item? Find(ScanResult result, RepoInfo repo, string path)
    {
        if (repo.Agents.Any(a => a.Path == path)) return new Item(path, path, path, false);
        if (repo.Skills.Any(s => s.Path == path) && path.EndsWith("/SKILL.md", StringComparison.Ordinal))
            return new Item(path, path[..^"/SKILL.md".Length], path, true);
        if (result.Workflows.Any(w => w.Repos.Any(r => r.RepoId == repo.Id && r.Path == path))) return new Item(path, path, path, false);
        return null;
    }

    /// <returns>null when the path is not an item of this repo in the scan</returns>
    public static VisibilityOutcome? Run(ScanResult result, RepoInfo repo, string path, string target, bool apply)
    {
        var item = Find(result, repo, path);
        if (item is null) return null;

        var root = Path.GetFullPath(result.ScanRoot);
        var repoDir = GapCommands.DirOf(result, repo);
        if (!Directory.Exists(repoDir) || !SafeFs.IsInside(root, repoDir)) return null;

        var query = new VisibilityQuery("x", item.TrackedPath, item.IgnorePath);
        var current = GitVisibility.Resolve(repoDir, [query])["x"];
        var excludeFile = GitExclude.ResolveFile(repoDir);

        VisibilityOutcome Blocked(string why) => new(path, current, target, [], why, [], false, current);
        if (current == Visibilities.Unknown || excludeFile is null) return Blocked("no-git");

        var entry = GitExclude.Entry(item.TrackedPath, item.IsDirectory);
        var oldText = File.Exists(excludeFile) ? File.ReadAllText(excludeFile) : null;
        var listed = GitExclude.Entries(oldText).Contains(entry);

        var changes = new List<VisibilityChange>();
        var notes = new List<string>();
        string? newText = oldText;
        var removeFromIndex = false;
        if (target == Visibilities.Private)
        {
            if (current != Visibilities.Private)
            {
                if (!listed)
                {
                    newText = GitExclude.Add(oldText, entry);
                    if (newText is null) return Blocked("exclude-damaged");
                    changes.Add(new VisibilityChange("exclude-add", entry));
                }
                if (current == Visibilities.Public)
                {
                    removeFromIndex = true;
                    changes.Add(new VisibilityChange("git-rm-cached", $"git rm --cached{(item.IsDirectory ? " -r" : "")} -- {item.TrackedPath}"));
                    notes.Add("commit-removal");
                }
            }
        }
        else if (current == Visibilities.Private)
        {
            if (!listed) return Blocked("other-rule"); // hidden by a .gitignore or a line RADAR did not write
            newText = GitExclude.Remove(oldText, entry);
            if (newText is null) return Blocked("exclude-damaged");
            changes.Add(new VisibilityChange("exclude-remove", entry));
            notes.Add("commit-to-share");
        }

        if (!apply || changes.Count == 0) return new VisibilityOutcome(path, current, target, changes, null, notes, false, current);

        if (newText != oldText) WriteExclude(excludeFile, newText ?? string.Empty);
        if (removeFromIndex)
        {
            var args = new List<string> { "rm", "--cached", "-q", "--ignore-unmatch" };
            if (item.IsDirectory) args.Add("-r");
            args.Add("--");
            args.Add(item.TrackedPath);
            var git = GitProcess.Run(repoDir, args);
            if (git is not { ExitCode: 0 })
            {
                Restore(excludeFile, oldText);
                throw new VisibilityFailure(Infrastructure.Localization.Msg.VisibilityGitFailed, git?.Error.Trim() is { Length: > 0 } t ? t : "git rm");
            }
        }

        var after = GitVisibility.Resolve(repoDir, [query])["x"];
        if (target == Visibilities.Public && after == Visibilities.Private)
        {
            // something else still hides it (a .gitignore pattern): put the file back as it was
            Restore(excludeFile, oldText);
            return Blocked("other-rule");
        }
        return new VisibilityOutcome(path, current, target, changes, null, notes, true, after);
    }

    private static void WriteExclude(string file, string text)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            var tmp = file + ".radar-tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            File.Move(tmp, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new VisibilityFailure(Infrastructure.Localization.Msg.VisibilityWriteFailed);
        }
    }

    private static void Restore(string file, string? oldText)
    {
        try
        {
            if (oldText is null) File.Delete(file);
            else File.WriteAllText(file, oldText, new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* best effort */ }
    }

    /// <summary>
    /// Puts a path that is not in a scan yet (a file or folder the app is about to create) on the private list.
    /// Returns null on success (or when <paramref name="apply"/> is false and it would work), otherwise the reason: no-git | exclude-damaged.
    /// </summary>
    public static string? Hide(string repoDir, string relativePath, bool isDirectory, bool apply)
    {
        var file = GitExclude.ResolveFile(repoDir);
        if (file is null) return "no-git";
        var old = File.Exists(file) ? File.ReadAllText(file) : null;
        var text = GitExclude.Add(old, GitExclude.Entry(relativePath, isDirectory));
        if (text is null) return "exclude-damaged";
        if (apply && text != old) WriteExclude(file, text);
        return null;
    }
}
