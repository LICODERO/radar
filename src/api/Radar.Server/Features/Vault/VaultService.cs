using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Radar.Scanner;
using Radar.Server.Features.Gaps;
using Radar.Server.Infrastructure.Storage;

namespace Radar.Server.Features.Vault;

public static class VaultFiles
{
    public const string Marker = ".radar-vault.json";
    public const string IndexMain = "_indexMain.md";
    public const string ProjectIndex = "_index.md";
    public const int CurrentVersion = 1;
    public static readonly string[] ProjectFolders = ["raw", "memory", "outputs"];
}

public sealed record VaultMarker(string App, string Kind, int Version, DateTimeOffset CreatedAt);

public sealed record VaultProject(string Name, string? RepoId);

/// <param name="State">"none" (not set up), "ok" or "missing" (a path is saved but the folder is gone or no longer a vault)</param>
/// <param name="Reason">for "missing": "folder-missing", "not-a-vault" or "unsupported-version"</param>
public sealed record VaultStatus(string State, string? Path, string? Reason, IReadOnlyList<VaultProject> Projects)
{
    public bool IsOk => State == "ok";
}

public enum VaultOutcome { Ok, PathInvalid, PathIsRoot, FolderMissing, InsideRepo, NotEmpty, AlreadyVault, NotAVault, UnsupportedVersion, WriteFailed }

public sealed record VaultResult(VaultOutcome Outcome, VaultStatus? Status = null);

public enum EnableOutcome { Ok, VaultNotReady, RepoMissing, RepoOutsideRoot, NameTaken, Forbidden, BlockDamaged, WriteFailed }

/// <param name="Creates">vault paths that do not exist yet and would be created</param>
/// <param name="GitIgnored">whether git ignores CLAUDE.local.md in the repo; null when git could not tell</param>
public sealed record ProjectPlan(string VaultDir, IReadOnlyList<string> Creates, string ClaudeLocalPath, bool ClaudeLocalExists, string Block, bool AlreadyEnabled, bool? GitIgnored);

public sealed record EnableResult(EnableOutcome Outcome, ProjectPlan? Plan = null, bool Applied = false);

/// <summary>
/// The second brain vault: a folder outside any repo with an index, one folder per project (raw / memory / outputs) and a marker file.
/// The saved path is only trusted while the marker is still there, so a moved folder shows up as "missing" instead of being recreated.
/// </summary>
public sealed partial class VaultService(ISettingsStore settings)
{
    public const string BlockStart = "<!-- radar:second-brain -->";
    public const string BlockEnd = "<!-- /radar:second-brain -->";
    private const string ProjectsStart = "<!-- radar:projects -->";
    private const string ProjectsEnd = "<!-- /radar:projects -->";
    private const int MaxClaudeLocalBytes = 1024 * 1024;

    public VaultStatus GetStatus()
    {
        var path = settings.Load().VaultPath;
        return string.IsNullOrWhiteSpace(path) ? new VaultStatus("none", null, null, []) : Inspect(path);
    }

    /// <summary>Creates a new vault in an empty (or not yet existing) folder and remembers it.</summary>
    public VaultResult Create(string rawPath)
    {
        var (full, error) = Resolve(rawPath);
        if (full is null) return new VaultResult(error);
        if (File.Exists(full)) return new VaultResult(VaultOutcome.PathInvalid);
        if (Directory.Exists(full))
        {
            if (File.Exists(Path.Combine(full, VaultFiles.Marker))) return new VaultResult(VaultOutcome.AlreadyVault);
            if (Directory.EnumerateFileSystemEntries(full).Any()) return new VaultResult(VaultOutcome.NotEmpty);
        }

        try
        {
            Directory.CreateDirectory(full);
            var marker = new VaultMarker("R.A.D.A.R.", "second-brain", VaultFiles.CurrentVersion, DateTimeOffset.UtcNow);
            WriteNew(Path.Combine(full, VaultFiles.Marker), JsonSerializer.Serialize(marker, RadarJson.Options));
            WriteNew(Path.Combine(full, VaultFiles.IndexMain), VaultTemplates.IndexMain());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new VaultResult(VaultOutcome.WriteFailed); }

        Remember(full);
        return new VaultResult(VaultOutcome.Ok, GetStatus());
    }

    /// <summary>Points the app at an existing vault (for instance after the folder was moved by hand).</summary>
    public VaultResult Relink(string rawPath)
    {
        var (full, error) = Resolve(rawPath);
        if (full is null) return new VaultResult(error);
        if (!Directory.Exists(full)) return new VaultResult(VaultOutcome.FolderMissing);

        var status = Inspect(full);
        if (!status.IsOk) return new VaultResult(status.Reason == "unsupported-version" ? VaultOutcome.UnsupportedVersion : VaultOutcome.NotAVault);

        Remember(full);
        return new VaultResult(VaultOutcome.Ok, status);
    }

    /// <summary>
    /// Turns the second brain on for one repo: the project folder in the vault (raw / memory / outputs and an index) and a marked
    /// block in the repo's CLAUDE.local.md that says where the vault is. Without <paramref name="apply"/> nothing is written
    /// and the plan is returned for the user to confirm.
    /// </summary>
    public EnableResult Enable(ScanResult scan, RepoInfo repo, bool apply)
    {
        var status = GetStatus();
        if (!status.IsOk || status.Path is null) return new EnableResult(EnableOutcome.VaultNotReady);
        var vault = status.Path;

        var repoDir = GapCommands.DirOf(scan, repo);
        if (!Directory.Exists(repoDir)) return new EnableResult(EnableOutcome.RepoMissing);
        if (!SafeFs.IsInside(Path.GetFullPath(scan.ScanRoot), repoDir)) return new EnableResult(EnableOutcome.RepoOutsideRoot);

        var projectDir = Path.Combine(vault, FolderName(repo.Name));
        var indexFile = Path.Combine(projectDir, VaultFiles.ProjectIndex);
        if (!SafeFs.IsInside(vault, projectDir)) return new EnableResult(EnableOutcome.Forbidden);
        var existingId = File.Exists(indexFile) ? ReadProjectId(indexFile) : null;
        if (existingId is not null && existingId != repo.Id) return new EnableResult(EnableOutcome.NameTaken);

        var claudeLocal = Path.Combine(repoDir, "CLAUDE.local.md");
        if (Directory.Exists(claudeLocal) || !SafeFs.IsInside(repoDir, claudeLocal)) return new EnableResult(EnableOutcome.Forbidden);
        var claudeLocalExists = File.Exists(claudeLocal);

        var creates = new List<string>();
        if (!Directory.Exists(projectDir)) creates.Add(projectDir);
        creates.AddRange(VaultFiles.ProjectFolders.Select(f => Path.Combine(projectDir, f)).Where(d => !Directory.Exists(d)));
        if (!File.Exists(indexFile)) creates.Add(indexFile);

        var block = BlockText(vault, projectDir);
        var plan = new ProjectPlan(projectDir, creates, claudeLocal, claudeLocalExists, block, existingId == repo.Id && creates.Count == 0,
            GitIgnore.IsIgnored(repoDir, "CLAUDE.local.md"));
        if (!apply) return new EnableResult(EnableOutcome.Ok, plan);

        try
        {
            foreach (var folder in VaultFiles.ProjectFolders) Directory.CreateDirectory(Path.Combine(projectDir, folder));
            if (!File.Exists(indexFile)) WriteNew(indexFile, VaultTemplates.ProjectIndex(repo.Id, repo.Name));
            UpdateProjectList(vault);

            string? current = null;
            if (claudeLocalExists)
            {
                if (new FileInfo(claudeLocal).Length > MaxClaudeLocalBytes) return new EnableResult(EnableOutcome.WriteFailed, plan);
                current = File.ReadAllText(claudeLocal);
            }
            var updated = MarkedBlock.Upsert(current, BlockStart, BlockEnd, block);
            if (updated is null) return new EnableResult(EnableOutcome.BlockDamaged, plan);
            if (updated != current) File.WriteAllText(claudeLocal, updated, new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new EnableResult(EnableOutcome.WriteFailed, plan); }

        return new EnableResult(EnableOutcome.Ok, plan with { Creates = [], AlreadyEnabled = true }, Applied: true);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static string BlockText(string vault, string projectDir) =>
        $"""
        ## Second brain

        This project's knowledge base lives outside the repository.

        - Vault: `{vault}`
        - Project folder: `{projectDir}`

        Start with `{Path.Combine(vault, VaultFiles.IndexMain)}` (how to use the vault), then `{Path.Combine(projectDir, VaultFiles.ProjectIndex)}`. `raw/` is read-only.
        """;

    /// <summary>A repo name as a folder name: letters, digits, dot, underscore and dash only.</summary>
    public static string FolderName(string repoName)
    {
        var chars = repoName.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-').ToArray();
        var name = new string(chars).Trim('.', '-');
        return name.Length == 0 ? "project" : name;
    }

    private static (string? Path, VaultOutcome Error) Resolve(string rawPath)
    {
        string full;
        try { full = AppPaths.Expand(rawPath); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return (null, VaultOutcome.PathInvalid); }
        if (Path.GetPathRoot(full) == full) return (null, VaultOutcome.PathIsRoot);
        if (IsInsideRepo(full)) return (null, VaultOutcome.InsideRepo);
        return (full, VaultOutcome.Ok);
    }

    /// <summary>True when the path, or a folder above it, is a git repo. The search stops at the home directory so a dotfiles repo in ~ does not block everything.</summary>
    public static bool IsInsideRepo(string path)
    {
        var home = Path.TrimEndingDirectorySeparator(AppPaths.Home);
        for (var dir = Path.TrimEndingDirectorySeparator(path); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            if (string.Equals(dir, home, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return false;
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git)) return true;
        }
        return false;
    }

    private static VaultStatus Inspect(string path)
    {
        if (!Directory.Exists(path)) return new VaultStatus("missing", path, "folder-missing", []);
        var marker = ReadMarker(path);
        if (marker is null) return new VaultStatus("missing", path, "not-a-vault", []);
        if (marker.Version > VaultFiles.CurrentVersion) return new VaultStatus("missing", path, "unsupported-version", []);
        return new VaultStatus("ok", path, null, ListProjects(path));
    }

    private static VaultMarker? ReadMarker(string vault)
    {
        try
        {
            var file = Path.Combine(vault, VaultFiles.Marker);
            if (!File.Exists(file) || new FileInfo(file).Length > 4096) return null;
            var marker = JsonSerializer.Deserialize<VaultMarker>(File.ReadAllBytes(file), RadarJson.Options);
            return marker is { Kind: "second-brain" } ? marker : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static IReadOnlyList<VaultProject> ListProjects(string vault)
    {
        try
        {
            return Directory.EnumerateDirectories(vault)
                .Where(d => !Path.GetFileName(d).StartsWith('.') && File.Exists(Path.Combine(d, VaultFiles.ProjectIndex)))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .Select(d => new VaultProject(Path.GetFileName(d), ReadProjectId(Path.Combine(d, VaultFiles.ProjectIndex))))
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    private static string? ReadProjectId(string indexFile)
    {
        try
        {
            using var reader = new StreamReader(indexFile);
            var head = new char[512];
            var read = reader.Read(head, 0, head.Length);
            var match = ProjectMarker().Match(new string(head, 0, read));
            return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    [GeneratedRegex(@"<!-- radar:project id=(\S+) -->")]
    private static partial Regex ProjectMarker();

    /// <summary>Rewrites the project list in _indexMain.md between its markers; a missing file is recreated, a file without markers is left alone.</summary>
    private static void UpdateProjectList(string vault)
    {
        var file = Path.Combine(vault, VaultFiles.IndexMain);
        if (!File.Exists(file)) WriteNew(file, VaultTemplates.IndexMain());
        var text = File.ReadAllText(file);
        if (!text.Contains(ProjectsStart, StringComparison.Ordinal) || !text.Contains(ProjectsEnd, StringComparison.Ordinal)) return;

        var projects = ListProjects(vault);
        var list = projects.Count == 0 ? "_No projects yet._" : string.Join("\n", projects.Select(p => $"- [{p.Name}]({p.Name}/{VaultFiles.ProjectIndex})"));
        var updated = MarkedBlock.Upsert(text, ProjectsStart, ProjectsEnd, list);
        if (updated is not null && updated != text) File.WriteAllText(file, updated, new UTF8Encoding(false));
    }

    private void Remember(string vaultPath) => settings.Save(settings.Load() with { VaultPath = vaultPath });

    /// <summary>Creates a file and refuses to overwrite an existing one.</summary>
    private static void WriteNew(string path, string content)
    {
        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        fs.Write(new UTF8Encoding(false).GetBytes(content.EndsWith('\n') ? content : content + "\n"));
    }
}
