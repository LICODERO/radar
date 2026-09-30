using Radar.Scanner;

namespace Radar.Server.Features.Files;

public sealed record FileContent(string Path, string Content, bool Truncated, long Bytes);

public enum FileReadStatus { Ok, NoScan, UnknownRepo, NotInScan, Forbidden, Missing }

public sealed record FileReadResult(FileReadStatus Status, FileContent? Content = null);

/// <summary>
/// Read-only access to the AI files found by the latest scan. A file can only be read when it is one of the
/// paths the scan reported for that repo, it is markdown, and it does not escape the repo through a symlink.
/// </summary>
public static class FileReader
{
    public static IReadOnlySet<string> KnownPaths(ScanResult result, RepoInfo repo)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (repo.ClaudeMd.Exists) set.Add(repo.ClaudeMd.Path);
        foreach (var a in repo.Agents) set.Add(a.Path);
        foreach (var s in repo.Skills) set.Add(s.Path);
        foreach (var w in result.Workflows)
            foreach (var r in w.Repos)
                if (r.RepoId == repo.Id) set.Add(r.Path);
        return set;
    }

    public static FileReadResult Read(ScanResult? result, string repoId, string path)
    {
        if (result is null) return new FileReadResult(FileReadStatus.NoScan);
        var repo = result.Repos.FirstOrDefault(r => r.Id == repoId);
        if (repo is null) return new FileReadResult(FileReadStatus.UnknownRepo);
        if (!KnownPaths(result, repo).Contains(path)) return new FileReadResult(FileReadStatus.NotInScan);
        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return new FileReadResult(FileReadStatus.NotInScan);

        var root = Path.GetFullPath(result.ScanRoot);
        var repoRoot = Path.GetFullPath(Path.Combine(root, repo.Path.Replace('/', Path.DirectorySeparatorChar)));
        var full = Path.GetFullPath(Path.Combine(repoRoot, path.Replace('/', Path.DirectorySeparatorChar)));

        // the repo must sit inside the scan root and the file inside the repo, with no symlink pointing out
        if (!SafeFs.IsInside(root, repoRoot) || !SafeFs.IsInside(repoRoot, full)) return new FileReadResult(FileReadStatus.Forbidden);
        if (!File.Exists(full)) return new FileReadResult(FileReadStatus.Missing);

        var text = SafeFs.ReadText(repoRoot, full);
        if (text is null) return new FileReadResult(FileReadStatus.Forbidden);
        var length = new FileInfo(full).Length;
        return new FileReadResult(FileReadStatus.Ok, new FileContent(path, text, length > SafeFs.MaxFileBytes, length));
    }
}
