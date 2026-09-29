using Radar.Scanner;

namespace Radar.Scanner.Tests;

public class AnalyzerTests
{
    [Fact]
    public void Detects_claude_md_agents_skills_and_outputs()
    {
        using var t = new FixtureTree();
        var repo = t.Repo("orders-api");
        t.File("orders-api/CLAUDE.md", "# orders");
        t.File("orders-api/.claude/agents/code-reviewer.md", "---\nname: code-reviewer\ndescription: Reviews\ntools: Read, Grep\nmodel: sonnet\n---\n");
        t.File("orders-api/.claude/agents/plain.md", "no frontmatter");
        t.File("orders-api/.claude/skills/ef-migrations/SKILL.md", "---\nname: ef-migrations\ndescription: EF\n---\n");
        t.File("orders-api/.claude/skills/empty-dir/README.md", "not a skill");
        t.File("orders-api/.claude/memory/OUTPUTS.md", "# outputs");
        t.File("orders-api/.claude/memory/decision-1.md", "x");
        t.File("orders-api/.claude/memory/decision-2.md", "x");

        var a = RepoAnalyzer.Analyze(t.Root, repo);

        Assert.Equal("orders-api", a.Repo.Id);
        Assert.Equal("OA", a.Repo.Initials);
        Assert.True(a.Repo.ClaudeMd.Exists);
        Assert.Equal(2, a.Repo.Agents.Count);
        var cr = a.Repo.Agents.Single(x => x.Name == "code-reviewer");
        Assert.Equal(["Read", "Grep"], cr.Tools);
        Assert.Equal("sonnet", cr.Model);
        Assert.Equal(".claude/agents/code-reviewer.md", cr.Path);
        Assert.Contains(a.Repo.Agents, x => x.Name == "plain");
        Assert.Single(a.Repo.Skills);
        Assert.Equal(".claude/skills/ef-migrations/SKILL.md", a.Repo.Skills[0].Path);
        Assert.True(a.Repo.Outputs.Exists);
        Assert.Equal(2, a.Repo.Outputs.Notes);
        Assert.Equal(100, a.Repo.Coverage.Score);
        Assert.Empty(a.Repo.Gaps);
    }

    [Fact]
    public void Reports_gaps_for_an_empty_repo()
    {
        using var t = new FixtureTree();
        var repo = t.Repo("billing-api");
        var a = RepoAnalyzer.Analyze(t.Root, repo);
        Assert.Equal(0, a.Repo.Coverage.Score);
        Assert.Equal(
            [GapTypes.NoClaudeMd, GapTypes.NoAgents, GapTypes.NoSkills, GapTypes.NoOutputs],
            a.Repo.Gaps);
    }

    [Theory]
    [InlineData(true, 0, 0, false, 40)]
    [InlineData(true, 2, 0, false, 65)]
    [InlineData(false, 3, 4, false, 50)]
    [InlineData(true, 1, 1, true, 100)]
    [InlineData(false, 0, 0, true, 10)]
    public void Coverage_follows_the_weights(bool claude, int agents, int skills, bool outputs, int expected)
    {
        Assert.Equal(expected, Coverage.Compute(claude, agents, skills, outputs).Score);
    }

    [Fact]
    public void Detects_outputs_folder_variant()
    {
        using var t = new FixtureTree();
        var repo = t.Repo("r");
        t.File("r/OUTPUTS/a.md", "x");
        t.File("r/OUTPUTS/b.md", "x");
        var a = RepoAnalyzer.Analyze(t.Root, repo);
        Assert.True(a.Repo.Outputs.Exists);
        Assert.Equal(2, a.Repo.Outputs.Notes);
    }

    [Fact]
    public void Reads_workflows_and_checks_the_link_in_claude_md()
    {
        using var t = new FixtureTree();
        var repo = t.Repo("r");
        t.File("r/CLAUDE.md", "| commit | before commit | .claude/workflows/commit.md |");
        t.File("r/.claude/workflows/commit.md", "---\nname: commit\ndescription: How to commit\nwhen: Before each commit\n---\n");
        t.File("r/.claude/workflows/release.md", "---\ndescription: Release\nagents: [pipeline-fixer]\n---\n");

        var a = RepoAnalyzer.Analyze(t.Root, repo);

        var commit = a.Workflows.Single(w => w.Name == "commit");
        Assert.True(commit.Linked);
        Assert.Equal("Before each commit", commit.When);
        var release = a.Workflows.Single(w => w.Name == "release"); // name falls back to the file name
        Assert.False(release.Linked);
        Assert.Equal(["pipeline-fixer"], release.Agents);
        Assert.Contains(GapTypes.WorkflowNotLinked, a.Repo.Gaps);
    }

    [Fact]
    public void Warns_about_broken_frontmatter()
    {
        using var t = new FixtureTree();
        var repo = t.Repo("r");
        t.File("r/.claude/agents/bad.md", "---\nname: bad\nnever closed");
        var a = RepoAnalyzer.Analyze(t.Root, repo);
        Assert.Single(a.Warnings);
        Assert.Equal(".claude/agents/bad.md", a.Warnings[0].Path);
    }

    [Fact]
    public void Does_not_read_files_reached_through_a_symlink_leaving_the_repo()
    {
        if (OperatingSystem.IsWindows()) return;
        using var outside = new FixtureTree();
        outside.File("secret/SKILL.md", "---\nname: leaked\ndescription: SHOULD NOT BE READ\n---\n");
        using var t = new FixtureTree();
        var repo = t.Repo("r");
        t.Dir("r/.claude/skills");
        Directory.CreateSymbolicLink(t.Path_("r/.claude/skills/linked"), outside.Path_("secret"));

        var a = RepoAnalyzer.Analyze(t.Root, repo);

        var skill = Assert.Single(a.Repo.Skills);
        Assert.Equal("linked", skill.Name);          // name from the directory, not from the file
        Assert.DoesNotContain("SHOULD NOT", skill.Description);
        Assert.Single(a.Warnings);
    }

    [Theory]
    [InlineData("orders-api", "OA")]
    [InlineData("shared-ui", "SU")]
    [InlineData("db_scripts", "DS")]
    [InlineData("monolith", "MO")]
    [InlineData("x", "X")]
    public void Builds_initials(string name, string expected) => Assert.Equal(expected, RepoAnalyzer.InitialsOf(name));
}

public class StackTests
{
    private static (string Primary, IReadOnlyList<string> All) Detect(Action<FixtureTree> build)
    {
        using var t = new FixtureTree();
        var repo = t.Repo("r");
        build(t);
        return StackDetector.Detect(repo);
    }

    [Fact] public void Dotnet_from_csproj() => Assert.Equal(".NET", Detect(t => t.File("r/src/App/App.csproj")).Primary);
    [Fact] public void Dotnet_from_sln() => Assert.Equal(".NET", Detect(t => t.File("r/App.sln")).Primary);
    [Fact] public void Angular_wins_over_node() => Assert.Equal(["Angular"], Detect(t => { t.File("r/angular.json"); t.File("r/package.json"); }).All);
    [Fact] public void Node_without_angular() => Assert.Equal("Node", Detect(t => t.File("r/package.json")).Primary);
    [Fact] public void Sql_only_when_nothing_else() => Assert.Equal("SQL", Detect(t => t.File("r/scripts/001.sql")).Primary);
    [Fact] public void Sql_ignored_next_to_dotnet() => Assert.Equal([".NET"], Detect(t => { t.File("r/a.csproj"); t.File("r/db/001.sql"); }).All);
    [Fact] public void Yaml_from_azure_pipeline() => Assert.Equal("YAML", Detect(t => t.File("r/azure-pipelines.yml")).Primary);
    [Fact] public void Yaml_from_github_workflows() => Assert.Equal("YAML", Detect(t => t.File("r/.github/workflows/ci.yml")).Primary);
    [Fact] public void Monorepo_lists_all_stacks() => Assert.Equal([".NET", "Angular"], Detect(t => { t.File("r/api/Api.csproj"); t.File("r/web/angular.json"); }).All);
    [Fact] public void Unknown_stack() => Assert.Equal("Inne", Detect(t => t.File("r/readme.txt")).Primary);
    [Fact] public void Ignores_markers_inside_node_modules() => Assert.Equal("Inne", Detect(t => t.File("r/node_modules/x/angular.json")).Primary);
}
