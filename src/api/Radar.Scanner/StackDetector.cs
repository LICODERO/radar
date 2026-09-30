namespace Radar.Scanner;

public static class StackDetector
{
    public const string DotNet = ".NET";
    public const string Angular = "Angular";
    public const string Node = "Node";
    public const string Sql = "SQL";
    public const string Yaml = "YAML";
    public const string Other = "Inne";

    private static readonly string[] Order = [DotNet, Angular, Node, Sql, Yaml];

    /// <summary>Heuristic stack detection from marker files (depth &lt;= 2). Returns primary stack + all stacks.</summary>
    public static (string Primary, IReadOnlyList<string> All) Detect(string repoDir, CancellationToken ct = default)
    {
        bool dotnet = false, angular = false, node = false, sql = false, yaml = false;

        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((repoDir, 0));
        var budget = 5000; // upper bound of visited entries per repo
        while (stack.Count > 0 && budget > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = stack.Pop();
            try
            {
                foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    if (--budget <= 0) break;
                    var name = entry.Name;
                    if (entry is DirectoryInfo d)
                    {
                        if (RepoDiscovery.IgnoredDirs.Contains(name) || RepoDiscovery.IsSymlink(d)) continue;
                        if (name.Equals(".github", StringComparison.OrdinalIgnoreCase) && depth == 0
                            && Directory.Exists(Path.Combine(d.FullName, "workflows"))) yaml = true;
                        if (name.StartsWith('.')) continue;
                        if (name.Equals("pipelines", StringComparison.OrdinalIgnoreCase) && HasYaml(d.FullName)) yaml = true;
                        if (depth < 2) stack.Push((d.FullName, depth + 1));
                        continue;
                    }

                    var ext = Path.GetExtension(name).ToLowerInvariant();
                    if (ext is ".sln" or ".csproj" or ".fsproj" or ".slnx") dotnet = true;
                    else if (name.Equals("angular.json", StringComparison.OrdinalIgnoreCase)) angular = true;
                    else if (name.Equals("package.json", StringComparison.OrdinalIgnoreCase)) node = true;
                    else if (ext == ".sql") sql = true;
                    else if (name.StartsWith("azure-pipelines", StringComparison.OrdinalIgnoreCase)
                             || name.Equals(".gitlab-ci.yml", StringComparison.OrdinalIgnoreCase)
                             || name.Equals("Jenkinsfile", StringComparison.OrdinalIgnoreCase)) yaml = true;
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException) { }
        }

        // Node only when it is not an Angular app; SQL and YAML only when nothing more specific exists.
        var found = new HashSet<string>();
        if (dotnet) found.Add(DotNet);
        if (angular) found.Add(Angular);
        if (node && !angular) found.Add(Node);
        if (found.Count == 0 && sql) found.Add(Sql);
        if (found.Count == 0 && yaml) found.Add(Yaml);

        var all = Order.Where(found.Contains).ToList();
        return all.Count == 0 ? (Other, [Other]) : (all[0], all);
    }

    private static bool HasYaml(string dir)
    {
        try { return Directory.EnumerateFiles(dir).Any(f => Path.GetExtension(f) is ".yml" or ".yaml"); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return false; }
    }
}
