namespace Radar.Scanner;

public static class GapTypes
{
    public const string NoClaudeMd = "no-claude-md";
    public const string NoAgents = "no-agents";
    public const string NoSkills = "no-skills";
    public const string NoOutputs = "no-outputs";
    public const string WorkflowNotLinked = "workflow-not-linked";
}

public sealed record AgentInfo(string Name, string Description, IReadOnlyList<string> Tools, string? Model, string Path);

public sealed record SkillInfo(string Name, string Description, string Path);

public sealed record ClaudeMdInfo(bool Exists, string Path);

public sealed record OutputsInfo(bool Exists, string Path, int Notes);

public sealed record CoverageParts(int ClaudeMd, int Agents, int Skills, int Outputs);

public sealed record CoverageInfo(int Score, CoverageParts Parts);

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
    OutputsInfo Outputs,
    CoverageInfo Coverage,
    IReadOnlyList<string> Gaps);

public sealed record WorkflowRepoRef(string RepoId, string Path, bool Linked);

public sealed record WorkflowInfo(
    string Id,
    string Name,
    string Description,
    string When,
    IReadOnlyList<string> Agents,
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
    int AvgCoverage);

public sealed record ScanResult(
    int SchemaVersion,
    string ScanRoot,
    DateTimeOffset ScannedAt,
    long DurationMs,
    SummaryInfo Summary,
    IReadOnlyList<RepoInfo> Repos,
    IReadOnlyList<WorkflowInfo> Workflows,
    IReadOnlyList<GapInfo> Gaps,
    IReadOnlyList<WarningInfo> Warnings)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record ScanOptions(string Root, int MaxDepth = 4);
