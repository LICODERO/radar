using Radar.Scanner;
using Radar.Server.Features.Agents;
using Radar.Server.Features.Gaps;
using Radar.Server.Features.Visibility;

namespace Radar.Server.Features.Skills;

/// <summary>Copies a skill folder into other scanned repos as a brand-new <c>.claude/skills/&lt;name&gt;/</c>, never overwriting anything.</summary>
public static class SkillCopy
{
    public const int MaxTargets = 100;

    public static IReadOnlyList<CopyTarget> Run(ScanResult result, string sourceRepoId, SkillBundle bundle, IReadOnlyList<string> targetIds, bool write, bool hide)
    {
        var targets = new List<CopyTarget>();
        foreach (var id in targetIds)
        {
            var repo = result.Repos.FirstOrDefault(r => r.Id == id);
            if (repo is null) { targets.Add(new CopyTarget(id, "unknown-repo", null)); continue; }
            var dir = GapCommands.DirOf(result, repo);
            var rel = $".claude/skills/{bundle.DirName}";
            if (id == sourceRepoId || Exists(repo, dir, bundle)) { targets.Add(new CopyTarget(id, "exists", null)); continue; }
            if (hide && Directory.Exists(dir) && ItemVisibility.Hide(dir, rel, true, apply: false) is not null) { targets.Add(new CopyTarget(id, "cannot-hide", null)); continue; }
            if (!write) { targets.Add(new CopyTarget(id, "ready", rel + "/SKILL.md")); continue; }

            var w = SkillWriter.WriteNew(result, repo, bundle);
            var hidden = false;
            if (hide && w.Status == WriteStatus.Created)
            {
                try { hidden = ItemVisibility.Hide(dir, rel, true, apply: true) is null; }
                catch (VisibilityFailure) { hidden = false; }
                if (!hidden)
                {
                    SkillWriter.TryRemove(dir, bundle.DirName);
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

    public static bool Exists(RepoInfo repo, string repoDir, SkillBundle bundle) =>
        repo.Skills.Any(s => s.Name.Equals(bundle.Name, StringComparison.OrdinalIgnoreCase))
        || Directory.Exists(Path.Combine(repoDir, ".claude", "skills", bundle.DirName))
        || File.Exists(Path.Combine(repoDir, ".claude", "skills", bundle.DirName));
}

/// <summary>Writes a new skill folder into a scanned repo. The folder must not exist; if anything fails halfway the folder (created here) is removed again.</summary>
public static class SkillWriter
{
    public static WriteResult WriteNew(ScanResult result, RepoInfo repo, SkillBundle bundle)
    {
        var root = Path.GetFullPath(result.ScanRoot);
        var repoDir = GapCommands.DirOf(result, repo);
        if (!Directory.Exists(repoDir)) return new WriteResult(WriteStatus.RepoMissing);
        if (!SafeFs.IsInside(root, repoDir)) return new WriteResult(WriteStatus.Forbidden);

        var claudeDir = Path.Combine(repoDir, ".claude");
        var skillsDir = Path.Combine(claudeDir, "skills");
        if ((Directory.Exists(claudeDir) && !SafeFs.IsInside(repoDir, claudeDir)) || (Directory.Exists(skillsDir) && !SafeFs.IsInside(repoDir, skillsDir)))
            return new WriteResult(WriteStatus.Forbidden);

        Directory.CreateDirectory(skillsDir);
        if (!SafeFs.IsInside(repoDir, skillsDir)) return new WriteResult(WriteStatus.Forbidden);

        var skillsFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(skillsDir));
        var target = Path.GetFullPath(Path.Combine(skillsFull, bundle.DirName));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!target.StartsWith(skillsFull + Path.DirectorySeparatorChar, comparison)) return new WriteResult(WriteStatus.Forbidden);
        if (Directory.Exists(target) || File.Exists(target)) return new WriteResult(WriteStatus.Exists);

        try
        {
            Directory.CreateDirectory(target);
            foreach (var file in bundle.Files)
            {
                var full = Path.GetFullPath(Path.Combine(target, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!full.StartsWith(target + Path.DirectorySeparatorChar, comparison)) throw new IOException("path leaves the skill folder");
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                using (var fs = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None)) fs.Write(file.Bytes);
                if (file.Executable && !OperatingSystem.IsWindows())
                    File.SetUnixFileMode(full, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryRemove(repoDir, bundle.DirName);
            return new WriteResult(WriteStatus.Forbidden);
        }
        return new WriteResult(WriteStatus.Created, $".claude/skills/{bundle.DirName}/SKILL.md");
    }

    /// <summary>Removes a skill folder this app has just created (best effort).</summary>
    public static void TryRemove(string repoDir, string dirName)
    {
        try { Directory.Delete(Path.Combine(repoDir, ".claude", "skills", dirName), true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* best effort */ }
    }
}
