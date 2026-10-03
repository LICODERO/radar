using Radar.Scanner;

namespace Radar.Server.Features.Gaps;

public sealed record GapItem(string RepoId, string RepoName, string Initials, string Type, string Dir, string Prompt);

/// <summary>Prompts for filling gaps. The server is the single source of truth: the UI only displays them and the run endpoint uses them.</summary>
public static class GapCommands
{
    public static readonly string[] Order =
        [GapTypes.NoClaudeMd, GapTypes.NoAgents, GapTypes.NoSkills, GapTypes.WorkflowNotLinked, GapTypes.WeakFiles];

    public static string DirOf(ScanResult result, RepoInfo repo) =>
        Path.GetFullPath(Path.Combine(result.ScanRoot, repo.Path.Replace('/', Path.DirectorySeparatorChar)));

    public static IReadOnlyList<GapItem> Build(ScanResult result)
    {
        var items = new List<GapItem>();
        foreach (var repo in result.Repos)
        {
            foreach (var type in Order)
            {
                if (!repo.Gaps.Contains(type)) continue;
                items.Add(Item(result, repo, type));
            }
        }
        return items;
    }

    public static GapItem Item(ScanResult result, RepoInfo repo, string type)
    {
        var unlinked = type == GapTypes.WorkflowNotLinked
            ? result.Workflows.SelectMany(w => w.Repos.Where(r => r.RepoId == repo.Id && !r.Linked).Select(r => r.Path)).ToList()
            : [];
        return new GapItem(repo.Id, repo.Name, repo.Initials, type, DirOf(result, repo), PromptFor(type, unlinked, repo.Quality?.Findings ?? []));
    }

    public static string PromptFor(string type, IReadOnlyList<string> unlinkedWorkflows, IReadOnlyList<QualityFinding>? findings = null) => type switch
    {
        GapTypes.NoClaudeMd =>
            "Analyze this repository and create a CLAUDE.md in its root. Include: what the project is, the tech stack, the exact build, test and run commands you can verify from the repository files, the architecture and folder structure, and the coding conventions you can infer. Do not invent commands or facts that are not visible in the repository. Keep it concise (under 120 lines) and do not modify any other file.",
        GapTypes.NoAgents =>
            "Create the directory .claude/agents and add 1-3 subagents that would be useful in this repository (for example a code reviewer and a test writer), based on its stack and conventions. Each agent is a Markdown file with the frontmatter fields name, description and tools, followed by short instructions. Do not modify any other file.",
        GapTypes.NoSkills =>
            "Create the directory .claude/skills and add 1-3 skills for recurring procedures in this repository (for example build and test, database migrations, conventions). Each skill is a folder with a SKILL.md that has the frontmatter fields name and description followed by concrete steps. Base them only on what exists in the repository and do not modify any other file.",
        GapTypes.WorkflowNotLinked =>
            $"CLAUDE.md does not mention these workflow files: {string.Join(", ", unlinkedWorkflows)}. Read each one and add a short table to CLAUDE.md (create the file if it does not exist) with the columns workflow, when to use, file path, taking the name and when fields from the frontmatter. Reference the files by path only and do not import them with @. Do not modify any other file.",
        GapTypes.WeakFiles => WeakFilesPrompt(findings ?? []),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown gap type")
    };

    private const int MaxFindingsInPrompt = 30;

    /// <summary>Fix prompt for a repo whose existing files are weak: the checks that failed, grouped by file, in plain English.</summary>
    private static string WeakFilesPrompt(IReadOnlyList<QualityFinding> findings)
    {
        var shown = findings.Where(f => f.Severity != Severities.Info).Concat(findings.Where(f => f.Severity == Severities.Info))
            .Take(MaxFindingsInPrompt).GroupBy(f => f.Path).Select(g => $"- {g.Key}: {string.Join("; ", g.Select(QualityText.Describe))}");
        return "Improve the quality of the existing Claude Code files in this repository. An automated check found these problems:\n"
            + string.Join('\n', shown)
            + "\nRead each listed file and the repository itself, then fix the problems with real, verifiable content: for CLAUDE.md the exact build, test and run commands and the architecture; for agents a description that says when to delegate to them, a tools list and concrete instructions; for skills a description that says when the skill applies and concrete steps. Do not invent facts, do not touch files that are not listed, and keep CLAUDE.md under 300 lines.";
    }
}
