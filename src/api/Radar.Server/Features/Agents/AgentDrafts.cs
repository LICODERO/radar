using System.Text.RegularExpressions;
using System.Text;
using Radar.Scanner;
using Radar.Server.Features.Gaps;

namespace Radar.Server.Features.Agents;

public sealed record AgentValidation(string? Name, IReadOnlyList<string> Errors)
{
    public bool Valid => Errors.Count == 0;
}

/// <summary>What is sent to Claude when drafting an agent: only the user's description, the stack name and the existing agent names.</summary>
public static class AgentPrompt
{
    public const string SystemPrompt = "You write Claude Code subagent definition files. Reply with the file content only.";
    public const int MaxDescription = 2000;

    public static string Build(string description, string stack, IReadOnlyList<string> existingAgents) =>
        $"""
        Create a Claude Code subagent definition (a Markdown file with YAML frontmatter) for a software repository.

        Repository tech stack: {stack}
        Existing agents in this repository (do not duplicate them): {(existingAgents.Count == 0 ? "none" : string.Join(", ", existingAgents))}

        The user describes the agent they want (it may be in Polish):
        <description>
        {description}
        </description>

        Rules:
        - Reply with the file content only: no explanations and no code fence around the whole file.
        - The file starts with a frontmatter block containing: name, description, tools.
        - name: lowercase English kebab-case, 2-40 characters, different from the existing agents.
        - description: 1-2 sentences saying when Claude should delegate to this agent (start with "Use when" or "Use proactively when"), in the same language as the user's description.
        - tools: comma-separated, the smallest set that is enough. Prefer read-only tools (Read, Grep, Glob); add Edit, Write or Bash only when the task really needs them.
        - After the frontmatter: a short role statement, 4-8 concrete rules and the expected response format, in the same language as the user's description. Keep the whole file under 60 lines.
        """;
}

public static partial class AgentValidator
{
    public const int MaxBytes = 64 * 1024;

    [GeneratedRegex("^[a-z][a-z0-9-]{1,63}$")]
    private static partial Regex NameRegex();

    public static bool IsValidName(string? name) => name is not null && NameRegex().IsMatch(name);

    /// <summary>Removes chatter and code fences the model may put around the file and normalises line endings.</summary>
    public static string Normalize(string raw)
    {
        var text = raw.Replace("\r\n", "\n").Trim();
        var lines = text.Split('\n').ToList();

        // ```markdown ... ``` around the whole answer
        if (lines.Count > 1 && lines[0].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            lines.RemoveAt(0);
            var last = lines.FindLastIndex(l => l.Trim() == "```");
            if (last >= 0) lines.RemoveRange(last, lines.Count - last);
        }

        // text before the frontmatter
        var start = lines.FindIndex(l => l.Trim() == "---");
        if (start > 0) lines.RemoveRange(0, start);

        return string.Join("\n", lines).Trim() + "\n";
    }

    public static AgentValidation Validate(string content)
    {
        var errors = new List<string>();
        if (Encoding.UTF8.GetByteCount(content) > MaxBytes) errors.Add("Plik jest za duży (limit 64 KB).");

        var fm = Frontmatter.Parse(content);
        if (!fm.Present || !fm.Valid)
        {
            errors.Add("Brak poprawnego frontmattera: plik ma zaczynać się od bloku --- ... ---.");
            return new AgentValidation(null, errors);
        }

        var name = fm.Get("name");
        if (!IsValidName(name)) errors.Add("Pole name musi być małymi literami, cyframi i myślnikami (np. migration-reviewer), 2-64 znaki.");

        var description = fm.Get("description");
        if (description is null || description.Length < 10) errors.Add("Pole description jest wymagane (min. 10 znaków): kiedy używać agenta.");
        else if (description.Length > 1024) errors.Add("Pole description jest za długie (max 1024 znaki).");

        var body = BodyOf(content);
        if (body.Trim().Length < 20) errors.Add("Brak treści instrukcji po frontmatterze.");

        return new AgentValidation(IsValidName(name) ? name : null, errors);
    }

    private static string BodyOf(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var seen = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() != "---") continue;
            if (++seen == 2) return string.Join("\n", lines.Skip(i + 1));
        }
        return string.Empty;
    }
}

public enum WriteStatus { Created, Exists, Forbidden, RepoMissing }

public sealed record WriteResult(WriteStatus Status, string? RelativePath = null);

/// <summary>The only place where the app writes into a scanned repo: one brand-new agent file, never overwriting.</summary>
public static class AgentWriter
{
    public static bool Exists(ScanResult result, RepoInfo repo, string name) =>
        repo.Agents.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        || File.Exists(Path.Combine(GapCommands.DirOf(result, repo), ".claude", "agents", name + ".md"));

    public static WriteResult WriteNew(ScanResult result, RepoInfo repo, string name, string content)
    {
        if (!AgentValidator.IsValidName(name)) throw new ArgumentException("Invalid agent name", nameof(name));

        var root = Path.GetFullPath(result.ScanRoot);
        var repoDir = GapCommands.DirOf(result, repo);
        if (!Directory.Exists(repoDir)) return new WriteResult(WriteStatus.RepoMissing);
        if (!SafeFs.IsInside(root, repoDir)) return new WriteResult(WriteStatus.Forbidden);

        var claudeDir = Path.Combine(repoDir, ".claude");
        var agentsDir = Path.Combine(claudeDir, "agents");
        // an existing .claude or agents directory that leads out of the repo through a symlink is refused
        if ((Directory.Exists(claudeDir) && !SafeFs.IsInside(repoDir, claudeDir))
            || (Directory.Exists(agentsDir) && !SafeFs.IsInside(repoDir, agentsDir)))
            return new WriteResult(WriteStatus.Forbidden);

        Directory.CreateDirectory(agentsDir);
        if (!SafeFs.IsInside(repoDir, agentsDir)) return new WriteResult(WriteStatus.Forbidden);

        var agentsFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(agentsDir));
        var target = Path.GetFullPath(Path.Combine(agentsFull, name + ".md"));
        if (!target.StartsWith(agentsFull + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return new WriteResult(WriteStatus.Forbidden);

        var bytes = new UTF8Encoding(false).GetBytes(content.Replace("\r\n", "\n").TrimEnd() + "\n");
        try
        {
            using var fs = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            fs.Write(bytes);
        }
        catch (IOException) when (File.Exists(target))
        {
            return new WriteResult(WriteStatus.Exists);
        }
        return new WriteResult(WriteStatus.Created, $".claude/agents/{name}.md");
    }
}
