using Radar.Scanner;

namespace Radar.Server.Features.Agents;

/// <param name="Status">ready (plan only) | created | exists | unknown-repo | repo-missing | forbidden</param>
public sealed record CopyTarget(string RepoId, string Status, string? Path);

/// <summary>Copies a validated agent file into other scanned repos through <see cref="AgentWriter"/>, so it can only ever add a new file.</summary>
public static class AgentCopy
{
    public const int MaxTargets = 100;

    public static IReadOnlyList<CopyTarget> Run(ScanResult result, string sourceRepoId, string name, string content, IReadOnlyList<string> targetIds, bool write)
    {
        var targets = new List<CopyTarget>();
        foreach (var id in targetIds)
        {
            var repo = result.Repos.FirstOrDefault(r => r.Id == id);
            if (repo is null) { targets.Add(new CopyTarget(id, "unknown-repo", null)); continue; }
            if (id == sourceRepoId || AgentWriter.Exists(result, repo, name)) { targets.Add(new CopyTarget(id, "exists", null)); continue; }
            if (!write) { targets.Add(new CopyTarget(id, "ready", $".claude/agents/{name}.md")); continue; }

            var w = AgentWriter.WriteNew(result, repo, name, content);
            targets.Add(new CopyTarget(id, w.Status switch
            {
                WriteStatus.Created => "created",
                WriteStatus.Exists => "exists",
                WriteStatus.RepoMissing => "repo-missing",
                _ => "forbidden"
            }, w.RelativePath));
        }
        return targets;
    }
}
