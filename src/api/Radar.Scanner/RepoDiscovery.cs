namespace Radar.Scanner;

public static class RepoDiscovery
{
    /// <summary>Directory names that are never entered (build output, dependencies, tooling).</summary>
    public static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", ".git", "dist", ".venv", "venv", ".next", "target", ".idea", ".vs",
        "packages", "__pycache__", ".gradle", ".angular", "out", "coverage", "TestResults"
    };

    public static bool IsRepo(string dir) => Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git"));

    public static bool IsSymlink(FileSystemInfo info) => info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint);

    /// <summary>
    /// Finds git repositories below <paramref name="root"/> (recursive up to <paramref name="maxDepth"/>).
    /// Does not descend into a found repository, into symlinked directories or into hidden/ignored directories.
    /// </summary>
    public static IEnumerable<string> Discover(string root, int maxDepth, CancellationToken ct = default)
    {
        var rootFull = Path.GetFullPath(root);
        if (!Directory.Exists(rootFull)) yield break;
        if (IsRepo(rootFull)) { yield return rootFull; yield break; }

        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((rootFull, 0));
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = stack.Pop();
            if (depth >= maxDepth) continue;

            List<DirectoryInfo> children;
            try
            {
                children = new DirectoryInfo(dir).EnumerateDirectories()
                    .Where(d => !d.Name.StartsWith('.') && !IgnoredDirs.Contains(d.Name) && !IsSymlink(d))
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            // push in reverse so the alphabetical order is preserved when popping
            var next = new List<string>();
            foreach (var c in children)
            {
                if (IsRepo(c.FullName)) yield return c.FullName;
                else next.Add(c.FullName);
            }
            for (var i = next.Count - 1; i >= 0; i--) stack.Push((next[i], depth + 1));
        }
    }
}
