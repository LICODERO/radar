namespace Radar.Scanner;

/// <summary>A workflow file as found in one repo, before aggregation across repos.</summary>
public sealed record RawWorkflow(string RepoId, string Name, string Description, string When, IReadOnlyList<string> Agents, IReadOnlyList<string> Skills, string Path, bool Linked);

public sealed record RepoAnalysis(RepoInfo Repo, IReadOnlyList<RawWorkflow> Workflows, IReadOnlyList<WarningInfo> Warnings);

/// <summary>Reads the AI setup of a single repository. Only markdown/AI files are opened; never secrets.</summary>
public static class RepoAnalyzer
{
    public static string InitialsOf(string name)
    {
        var parts = name.Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries);
        var s = parts.Length >= 2 ? $"{parts[0][0]}{parts[1][0]}" : (parts.Length == 1 ? parts[0][..Math.Min(2, parts[0].Length)] : "?");
        return s.ToUpperInvariant();
    }

    public static RepoAnalysis Analyze(string root, string repoDir, CancellationToken ct = default)
    {
        var id = Path.GetRelativePath(root, repoDir).Replace('\\', '/');
        if (id == ".") id = Path.GetFileName(Path.TrimEndingDirectorySeparator(repoDir));
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(repoDir));
        var warnings = new List<WarningInfo>();
        var claudeDir = Path.Combine(repoDir, ".claude");

        // CLAUDE.md
        var claudePath = Path.Combine(repoDir, "CLAUDE.md");
        var claudeExists = File.Exists(claudePath);
        var claudeText = claudeExists ? SafeFs.ReadText(repoDir, claudePath) : null;

        // agents
        var agents = new List<AgentInfo>();
        foreach (var file in SafeFs.EnumerateMarkdown(Path.Combine(claudeDir, "agents")))
        {
            ct.ThrowIfCancellationRequested();
            var rel = $".claude/agents/{Path.GetFileName(file)}";
            var text = SafeFs.ReadText(repoDir, file);
            if (text is null) warnings.Add(new WarningInfo(id, rel, "Nie można odczytać pliku (poza repozytorium lub brak dostępu)."));
            var fm = Frontmatter.Parse(text);
            if (fm.Present && !fm.Valid) warnings.Add(new WarningInfo(id, rel, "Nieprawidłowy frontmatter (brak zamknięcia ---)."));
            agents.Add(new AgentInfo(
                fm.Get("name") ?? Path.GetFileNameWithoutExtension(file),
                fm.Get("description") ?? string.Empty,
                fm.GetList("tools"),
                fm.Get("model"),
                rel));
        }

        // skills
        var skills = new List<SkillInfo>();
        var skillsDir = Path.Combine(claudeDir, "skills");
        foreach (var dir in SafeDirs(skillsDir))
        {
            ct.ThrowIfCancellationRequested();
            var skillFile = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(skillFile)) continue;
            var dirName = Path.GetFileName(dir);
            var rel = $".claude/skills/{dirName}/SKILL.md";
            var text = SafeFs.ReadText(repoDir, skillFile);
            if (text is null) warnings.Add(new WarningInfo(id, rel, "Nie można odczytać pliku (poza repozytorium lub brak dostępu)."));
            var fm = Frontmatter.Parse(text);
            if (fm.Present && !fm.Valid) warnings.Add(new WarningInfo(id, rel, "Nieprawidłowy frontmatter (brak zamknięcia ---)."));
            skills.Add(new SkillInfo(fm.Get("name") ?? dirName, fm.Get("description") ?? string.Empty, rel));
        }

        // workflows
        var workflows = new List<RawWorkflow>();
        foreach (var file in SafeFs.EnumerateMarkdown(Path.Combine(claudeDir, "workflows")))
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(file);
            var rel = $".claude/workflows/{fileName}";
            var text = SafeFs.ReadText(repoDir, file);
            var fm = Frontmatter.Parse(text);
            if (fm.Present && !fm.Valid) warnings.Add(new WarningInfo(id, rel, "Nieprawidłowy frontmatter (brak zamknięcia ---)."));
            var linked = claudeText is not null
                && (claudeText.Contains(rel, StringComparison.OrdinalIgnoreCase)
                    || claudeText.Contains("workflows/" + fileName, StringComparison.OrdinalIgnoreCase));
            workflows.Add(new RawWorkflow(
                id,
                fm.Get("name") ?? Path.GetFileNameWithoutExtension(file),
                fm.Get("description") ?? string.Empty,
                fm.Get("when") ?? fm.Get("trigger") ?? string.Empty,
                fm.GetList("agents"),
                fm.GetList("skills"),
                rel,
                linked));
        }

        // memory / OUTPUTS
        var outputs = DetectOutputs(repoDir);

        var (primary, stacks) = StackDetector.Detect(repoDir, ct);
        var coverage = Coverage.Compute(claudeExists, agents.Count, skills.Count, outputs.Exists);

        var gaps = new List<string>();
        if (!claudeExists) gaps.Add(GapTypes.NoClaudeMd);
        if (agents.Count == 0) gaps.Add(GapTypes.NoAgents);
        if (skills.Count == 0) gaps.Add(GapTypes.NoSkills);
        if (!outputs.Exists) gaps.Add(GapTypes.NoOutputs);
        if (workflows.Any(w => !w.Linked)) gaps.Add(GapTypes.WorkflowNotLinked);

        var repo = new RepoInfo(
            id, name, id, InitialsOf(name), primary, stacks,
            new ClaudeMdInfo(claudeExists, "CLAUDE.md"),
            agents, skills, outputs, coverage, gaps);
        return new RepoAnalysis(repo, workflows, warnings);
    }

    private static OutputsInfo DetectOutputs(string repoDir)
    {
        var memory = Path.Combine(repoDir, ".claude", "memory");
        var file = Path.Combine(memory, "OUTPUTS.md");
        if (File.Exists(file))
        {
            var notes = SafeFs.EnumerateMarkdown(memory)
                .Count(f => !Path.GetFileName(f).Equals("OUTPUTS.md", StringComparison.OrdinalIgnoreCase));
            return new OutputsInfo(true, ".claude/memory/OUTPUTS.md", notes);
        }

        var folder = Path.Combine(repoDir, "OUTPUTS");
        if (Directory.Exists(folder)) return new OutputsInfo(true, "OUTPUTS/", SafeFs.EnumerateMarkdown(folder).Count());
        return new OutputsInfo(false, ".claude/memory/OUTPUTS.md", 0);
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return [];
            return Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return []; }
    }
}
