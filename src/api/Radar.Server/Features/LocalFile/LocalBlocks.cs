using System.Text;
using System.Text.RegularExpressions;
using Radar.Scanner;

namespace Radar.Server.Features.LocalFile;

/// <summary>The texts of the blocks RADAR keeps in CLAUDE.local.md and CLAUDE.md. Short on purpose: both files are loaded into every session.</summary>
public static partial class LocalBlocks
{
    public const string WorkflowsStart = "<!-- radar:workflows -->";
    public const string WorkflowsEnd = "<!-- /radar:workflows -->";
    public const string AgentsStart = "<!-- radar:agents-md -->";
    public const string AgentsEnd = "<!-- /radar:agents-md -->";
    public const int MaxDescription = 160;

    public sealed record Entry(string Name, string Description, string When, string Path);

    /// <summary>
    /// The index of workflows. Claude finds agents and skills on its own, but a workflow is a convention of this project, so it only
    /// knows about it when a file it always reads says so.
    /// </summary>
    public static string Workflows(IReadOnlyList<Entry> entries, bool isPrivate)
    {
        var sb = new StringBuilder();
        sb.Append(isPrivate ? "## Workflows (private)\n\n" : "## Workflows\n\n");
        sb.Append("Project procedures. Before a task, check whether one of them applies and follow it.\n\n");
        foreach (var e in entries)
        {
            sb.Append("- `").Append(e.Name).Append('`');
            if (e.Description.Length > 0) sb.Append(": ").Append(Short(e.Description));
            if (e.When.Length > 0) sb.Append(" When: ").Append(Short(e.When));
            sb.Append(" File: `").Append(e.Path).Append("`\n");
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>The import that keeps Claude reading AGENTS.md: a CLAUDE.local.md (like a CLAUDE.md) makes Claude stop reading it on its own.</summary>
    public const string AgentsImport = "@AGENTS.md";

    /// <summary>True when the repo has an AGENTS.md and no CLAUDE.md of its own, so adding CLAUDE.local.md would silence AGENTS.md.</summary>
    public static bool NeedsAgentsImport(string repoDir) =>
        File.Exists(Path.Combine(repoDir, "AGENTS.md"))
        && !File.Exists(Path.Combine(repoDir, "CLAUDE.md"))
        && !File.Exists(Path.Combine(repoDir, ".claude", "CLAUDE.md"));

    private static string Short(string s)
    {
        var one = Whitespace().Replace(s, " ").Trim();
        return one.Length <= MaxDescription ? one : one[..(MaxDescription - 1)].TrimEnd() + "…";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
