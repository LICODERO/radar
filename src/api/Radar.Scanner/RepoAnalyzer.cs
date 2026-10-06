namespace Radar.Scanner;

/// <summary>A workflow file as found in one repo, before aggregation across repos.</summary>
public sealed record RawWorkflow(string RepoId, string Name, string Description, string When, IReadOnlyList<string> Agents, IReadOnlyList<string> Skills, string Path, bool Linked, string? Visibility = null);

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
        var localPath = Path.Combine(repoDir, "CLAUDE.local.md");
        var localText = File.Exists(localPath) ? SafeFs.ReadText(repoDir, localPath) : null;
        var findings = new List<QualityFinding>();
        var qualityFiles = new List<(string Path, string Kind)>();
        if (claudeText is not null)
        {
            qualityFiles.Add(("CLAUDE.md", FileKinds.ClaudeMd));
            findings.AddRange(Quality.ClaudeMd(repoDir, "CLAUDE.md", claudeText));
            if (Quality.Stale("CLAUDE.md", SafeFs.LastWriteUtc(repoDir, claudePath), Quality.RepoActivity(repoDir)) is { } stale) findings.Add(stale);
        }

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
            if (text is not null) qualityFiles.Add((rel, FileKinds.Agent));
            findings.AddRange(Quality.Agent(rel, Path.GetFileNameWithoutExtension(file), text, fm));
            agents.Add(new AgentInfo(
                fm.Get("name") ?? Path.GetFileNameWithoutExtension(file),
                fm.Get("description") ?? string.Empty,
                fm.GetList("tools"),
                fm.Get("model"),
                rel,
                ContentHash.Of(text)));
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
            if (text is not null) qualityFiles.Add((rel, FileKinds.Skill));
            findings.AddRange(Quality.Skill(rel, dirName, text, fm));
            skills.Add(new SkillInfo(fm.Get("name") ?? dirName, fm.Get("description") ?? string.Empty, rel, ContentHash.Of(text)));
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
            // a workflow is linked when CLAUDE.md or the private CLAUDE.local.md points at it, so Claude is told it exists
            var linked = new[] { claudeText, localText }.Any(t => t is not null
                && (t.Contains(rel, StringComparison.OrdinalIgnoreCase) || t.Contains("workflows/" + fileName, StringComparison.OrdinalIgnoreCase)));
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

        // visibility: shared (tracked), hidden (ignored) or not decided (untracked)
        var queries = agents.Select(a => new VisibilityQuery("a:" + a.Path, a.Path, a.Path))
            .Concat(skills.Select(k => new VisibilityQuery("s:" + k.Path, k.Path[..^"/SKILL.md".Length], k.Path)))
            .Concat(workflows.Select(w => new VisibilityQuery("w:" + w.Path, w.Path, w.Path)))
            .ToList();
        var visibility = GitVisibility.Resolve(repoDir, queries);
        agents = agents.Select(a => a with { Visibility = visibility[("a:" + a.Path)] }).ToList();
        skills = skills.Select(k => k with { Visibility = visibility[("s:" + k.Path)] }).ToList();
        workflows = workflows.Select(w => w with { Visibility = visibility[("w:" + w.Path)] }).ToList();

        var (primary, stacks) = StackDetector.Detect(repoDir, ct);
        var coverage = Coverage.Compute(claudeExists, agents.Count, skills.Count);

        findings.AddRange(Quality.DuplicateAgents(agents));
        var quality = Quality.Summarize(qualityFiles, findings);

        var gaps = new List<string>();
        if (!claudeExists) gaps.Add(GapTypes.NoClaudeMd);
        if (agents.Count == 0) gaps.Add(GapTypes.NoAgents);
        if (skills.Count == 0) gaps.Add(GapTypes.NoSkills);
        if (workflows.Any(w => !w.Linked)) gaps.Add(GapTypes.WorkflowNotLinked);
        if (Quality.IsWeak(quality)) gaps.Add(GapTypes.WeakFiles);

        var repo = new RepoInfo(
            id, name, id, InitialsOf(name), primary, stacks,
            new ClaudeMdInfo(claudeExists, "CLAUDE.md"),
            agents, skills, coverage, gaps, quality);
        return new RepoAnalysis(repo, workflows, warnings);
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
