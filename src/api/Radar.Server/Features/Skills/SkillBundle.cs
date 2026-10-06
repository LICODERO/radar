using System.Text;
using Radar.Scanner;
using Radar.Server.Features.Agents;
using Radar.Server.Features.Gaps;

namespace Radar.Server.Features.Skills;

public sealed record SkillFile(string RelativePath, byte[] Bytes, bool Executable);

/// <param name="Reason">symlink | binary | unreadable</param>
public sealed record SkippedFile(string Path, string Reason);

/// <param name="DirName">the skill's folder name under .claude/skills</param>
public sealed record SkillBundle(string DirName, string Name, IReadOnlyList<SkillFile> Files, IReadOnlyList<SkippedFile> Skipped)
{
    public long Bytes => Files.Sum(f => (long)f.Bytes.Length);
}

public enum BundleError { Invalid, TooManyFiles, TooBig, Unreadable }

public sealed record BundleResult(SkillBundle? Bundle, BundleError? Error);

/// <summary>
/// Reads a whole skill folder for copying. Only regular files inside the skill folder are taken: symlinks are skipped, and so are
/// binary files outside <c>scripts/</c> (they would be copied blindly into another repo). The size is capped.
/// </summary>
public static class SkillBundleReader
{
    public const int MaxFiles = 50;
    public const int MaxTotalBytes = 1024 * 1024;
    private const string ScriptsDir = "scripts/";

    public static BundleResult Read(ScanResult result, RepoInfo repo, SkillInfo skill)
    {
        const string suffix = "/SKILL.md";
        if (!skill.Path.EndsWith(suffix, StringComparison.Ordinal)) return new BundleResult(null, BundleError.Invalid);
        var relDir = skill.Path[..^suffix.Length];
        var dirName = relDir[(relDir.LastIndexOf('/') + 1)..];
        if (!AgentValidator.IsValidName(dirName)) return new BundleResult(null, BundleError.Invalid);

        var repoDir = GapCommands.DirOf(result, repo);
        var dir = Path.GetFullPath(Path.Combine(repoDir, relDir.Replace('/', Path.DirectorySeparatorChar)));
        if (!Directory.Exists(dir) || !SafeFs.IsInside(repoDir, dir)) return new BundleResult(null, BundleError.Unreadable);

        var files = new List<SkillFile>();
        var skipped = new List<SkippedFile>();
        long total = 0;
        try
        {
            var stack = new Stack<string>();
            stack.Push(dir);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var entry in new DirectoryInfo(current).EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.Ordinal))
                {
                    var rel = Path.GetRelativePath(dir, entry.FullName).Replace('\\', '/');
                    if (entry.Name == ".DS_Store") continue;
                    if (entry.LinkTarget is not null || entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) { skipped.Add(new SkippedFile(rel, "symlink")); continue; }
                    if (entry is DirectoryInfo) { stack.Push(entry.FullName); continue; }

                    var info = (FileInfo)entry;
                    if (files.Count >= MaxFiles) return new BundleResult(null, BundleError.TooManyFiles);
                    if (total + info.Length > MaxTotalBytes) return new BundleResult(null, BundleError.TooBig);
                    var bytes = File.ReadAllBytes(info.FullName);
                    if (IsBinary(bytes) && !rel.StartsWith(ScriptsDir, StringComparison.Ordinal)) { skipped.Add(new SkippedFile(rel, "binary")); continue; }
                    total += bytes.Length;
                    files.Add(new SkillFile(rel, bytes, IsExecutable(info)));
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new BundleResult(null, BundleError.Unreadable); }

        var main = files.FirstOrDefault(f => f.RelativePath == "SKILL.md");
        if (main is null) return new BundleResult(null, BundleError.Invalid);
        var fm = Frontmatter.Parse(Encoding.UTF8.GetString(main.Bytes));
        if (!fm.Present || !fm.Valid) return new BundleResult(null, BundleError.Invalid);

        return new BundleResult(new SkillBundle(dirName, fm.Get("name") ?? dirName, files, skipped), null);
    }

    private static bool IsBinary(byte[] bytes)
    {
        var n = Math.Min(bytes.Length, 8000);
        for (var i = 0; i < n; i++) if (bytes[i] == 0) return true;
        return false;
    }

    private static bool IsExecutable(FileInfo file) =>
        !OperatingSystem.IsWindows() && (file.UnixFileMode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
}
