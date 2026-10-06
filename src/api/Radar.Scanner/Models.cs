namespace Radar.Scanner;

public static class GapTypes
{
    public const string NoClaudeMd = "no-claude-md";
    public const string NoAgents = "no-agents";
    public const string NoSkills = "no-skills";
    public const string WorkflowNotLinked = "workflow-not-linked";
    public const string WeakFiles = "weak-files";
}

/// <param name="Visibility">one of <see cref="Visibilities"/>; null when the scan did not check git</param>
/// <param name="Hash">short hash of the file content (line endings and outer blanks ignored), to tell copies of the same agent apart across repos</param>
public sealed record AgentInfo(string Name, string Description, IReadOnlyList<string> Tools, string? Model, string Path, string? Hash = null, string? Visibility = null);

public sealed record SkillInfo(string Name, string Description, string Path, string? Hash = null, string? Visibility = null);

public sealed record ClaudeMdInfo(bool Exists, string Path);

public sealed record CoverageParts(int ClaudeMd, int Agents, int Skills);

public sealed record CoverageInfo(int Score, CoverageParts Parts);

public static class FileKinds
{
    public const string ClaudeMd = "claude-md";
    public const string Agent = "agent";
    public const string Skill = "skill";
}

public static class Severities
{
    public const string Error = "error";
    public const string Warning = "warning";
    public const string Info = "info";
}

/// <param name="Path">repo-relative path of the file the finding is about</param>
/// <param name="Code">stable identifier (the UI and the prompts turn it into text)</param>
/// <param name="Detail">the offending value (a missing path, a line count...), when the code has one</param>
public sealed record QualityFinding(string Path, string Code, string Severity, string? Detail = null);

public sealed record FileQuality(string Path, string Kind, int Score);

/// <summary>How good the existing files are (coverage only says that they exist). <paramref name="Score"/> is null when the repo has no AI file at all.</summary>
public sealed record QualityInfo(int? Score, IReadOnlyList<FileQuality> Files, IReadOnlyList<QualityFinding> Findings);

public sealed record RepoInfo(
    string Id,
    string Name,
    string Path,
    string Initials,
    string Stack,
    IReadOnlyList<string> Stacks,
    ClaudeMdInfo ClaudeMd,
    IReadOnlyList<AgentInfo> Agents,
    IReadOnlyList<SkillInfo> Skills,
    CoverageInfo Coverage,
    IReadOnlyList<string> Gaps,
    QualityInfo? Quality = null,
    IReadOnlyList<NameConflict>? NameConflicts = null);

/// <param name="Agents">agents this repo's copy of the workflow names (they are elements of that same repo)</param>
/// <param name="Skills">skills this repo's copy of the workflow names</param>
public sealed record WorkflowRepoRef(string RepoId, string Path, bool Linked, IReadOnlyList<string> Agents, IReadOnlyList<string> Skills, string? Visibility = null);

public sealed record WorkflowInfo(
    string Id,
    string Name,
    string Description,
    string When,
    IReadOnlyList<string> Agents,
    IReadOnlyList<string> Skills,
    IReadOnlyList<WorkflowRepoRef> Repos,
    IReadOnlyList<string> Issues);

public sealed record GapInfo(string RepoId, string Type);

public sealed record WarningInfo(string RepoId, string Path, string Message);

public sealed record SummaryInfo(
    int Repos,
    int Agents,
    int Skills,
    int Workflows,
    int Gaps,
    IReadOnlyList<string> Stacks,
    int AvgCoverage,
    int? AvgQuality = null);

public sealed record ScanResult(
    int SchemaVersion,
    string ScanRoot,
    DateTimeOffset ScannedAt,
    long DurationMs,
    SummaryInfo Summary,
    IReadOnlyList<RepoInfo> Repos,
    IReadOnlyList<WorkflowInfo> Workflows,
    IReadOnlyList<GapInfo> Gaps,
    IReadOnlyList<WarningInfo> Warnings,
    IReadOnlyList<SharedItemInfo>? Shared = null)
{
    public const int CurrentSchemaVersion = 2;
}

/// <summary>The same content (one hash) and the repos that hold it.</summary>
public sealed record ItemVariant(string Hash, IReadOnlyList<string> Repos, string Path);

/// <summary>An agent or skill with the same name in two or more repos: are the copies the same, and who lacks it.</summary>
/// <param name="Missing">repos of the same stack as a holder that do not have it</param>
public sealed record SharedItemInfo(string Kind, string Name, IReadOnlyList<ItemVariant> Variants, IReadOnlyList<string> Missing);

public sealed record ScanOptions(string Root, int MaxDepth = 4);
