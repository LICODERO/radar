using Radar.Scanner;
using Radar.Server.Features.Gaps;
using Radar.Server.Features.Visibility;

namespace Radar.Server.Features.Agents;

/// <param name="Status">ready (plan only) | created | exists | unknown-repo | repo-missing | forbidden | cannot-hide</param>
/// <param name="Hidden">the new file was put on the private list</param>
public sealed record CopyTarget(string RepoId, string Status, string? Path, bool Hidden = false);

/// <summary>Copies a validated agent file into other scanned repos through <see cref="AgentWriter"/>, so it can only ever add a new file.</summary>
public static class AgentCopy
{
    public const int MaxTargets = 100;

    public static IReadOnlyList<CopyTarget> Run(ScanResult result, string sourceRepoId, string name, string content, IReadOnlyList<string> targetIds, bool write, bool hide = false)
    {
        var targets = new List<CopyTarget>();
        foreach (var id in targetIds)
        {
            var repo = result.Repos.FirstOrDefault(r => r.Id == id);
            if (repo is null) { targets.Add(new CopyTarget(id, "unknown-repo", null)); continue; }
            if (id == sourceRepoId || AgentWriter.Exists(result, repo, name)) { targets.Add(new CopyTarget(id, "exists", null)); continue; }
            var rel = $".claude/agents/{name}.md";
            var dir = GapCommands.DirOf(result, repo);
            // a private copy must be hideable before anything is written: never leave a file that git would offer for commit
            if (hide && Directory.Exists(dir) && ItemVisibility.Hide(dir, rel, false, apply: false) is not null) { targets.Add(new CopyTarget(id, "cannot-hide", null)); continue; }
            if (!write) { targets.Add(new CopyTarget(id, "ready", rel)); continue; }

            var w = AgentWriter.WriteNew(result, repo, name, content);
            var hidden = false;
            if (hide && w.Status == WriteStatus.Created)
            {
                try { hidden = ItemVisibility.Hide(dir, rel, false, apply: true) is null; }
                catch (VisibilityFailure) { hidden = false; }
                if (!hidden)
                {
                    TryDelete(Path.Combine(dir, ".claude", "agents", name + ".md"));
                    targets.Add(new CopyTarget(id, "cannot-hide", null));
                    continue;
                }
            }
            targets.Add(new CopyTarget(id, w.Status switch
            {
                WriteStatus.Created => "created",
                WriteStatus.Exists => "exists",
                WriteStatus.RepoMissing => "repo-missing",
                _ => "forbidden"
            }, w.RelativePath, hidden));
        }
        return targets;
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* best effort: the file was created a moment ago by us */ }
    }
}
