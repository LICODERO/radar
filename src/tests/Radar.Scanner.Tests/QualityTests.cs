using Radar.Scanner;

namespace Radar.Scanner.Tests;

public class QualityTests
{
    public const string GoodClaudeMd = """
        # orders-api

        ASP.NET Core service that stores customer orders.

        ## Commands

        ```bash
        dotnet build
        dotnet test
        dotnet run --project src/Orders.Api
        ```

        ## Architecture

        - `src/Orders.Api` HTTP endpoints
        - `src/Orders.Core` domain logic
        - `tests/` xUnit tests

        ## Conventions

        Use records for DTOs. Keep endpoints thin.
        """;

    public static string GoodAgent(string name) =>
        $"---\nname: {name}\ndescription: Reviews pull requests for correctness and style before merge\ntools: Read, Grep\nmodel: sonnet\n---\n\nYou review code.\nRead the diff first.\nCheck tests exist.\nReport findings by severity.\nBe concise.\n";

    public static string GoodSkill(string name) =>
        $"---\nname: {name}\ndescription: Use when adding or applying EF Core migrations to the orders database\n---\n\n1. Add the migration.\n2. Review the generated SQL.\n3. Apply it to the dev database.\n4. Run the tests.\n5. Commit both files.\n";

    private static QualityInfo Quality_(FixtureTree t, string repo) => RepoAnalyzer.Analyze(t.Root, t.Path_(repo)).Repo.Quality!;

    private static IEnumerable<string> Codes(QualityInfo q, string path) => q.Findings.Where(f => f.Path == path).Select(f => f.Code);

    [Fact]
    public void A_complete_setup_scores_100_and_has_no_gap()
    {
        using var t = new FixtureTree();
        t.Repo("orders-api");
        t.File("orders-api/CLAUDE.md", GoodClaudeMd);
        t.File("orders-api/.claude/agents/reviewer.md", GoodAgent("reviewer"));
        t.File("orders-api/.claude/skills/ef-migrations/SKILL.md", GoodSkill("ef-migrations"));

        var a = RepoAnalyzer.Analyze(t.Root, t.Path_("orders-api"));

        Assert.Empty(a.Repo.Quality!.Findings);
        Assert.Equal(100, a.Repo.Quality.Score);
        Assert.Equal(3, a.Repo.Quality.Files.Count);
        Assert.Empty(a.Repo.Gaps);
    }

    [Fact]
    public void A_repo_without_ai_files_has_no_quality_score()
    {
        using var t = new FixtureTree();
        t.Repo("bare");
        var q = Quality_(t, "bare");
        Assert.Null(q.Score);
        Assert.Empty(q.Files);
    }

    [Fact]
    public void A_stub_claude_md_is_an_error_and_makes_the_repo_weak()
    {
        using var t = new FixtureTree();
        t.Repo("r");
        t.File("r/CLAUDE.md", "# r\n");
        var a = RepoAnalyzer.Analyze(t.Root, t.Path_("r"));
        var f = Assert.Single(a.Repo.Quality!.Findings, x => x.Code == "claude-md-thin");
        Assert.Equal(Severities.Error, f.Severity);
        Assert.Contains(GapTypes.WeakFiles, a.Repo.Gaps);
    }

    [Fact]
    public void Claude_md_without_commands_structure_or_with_placeholders_is_flagged()
    {
        using var t = new FixtureTree();
        t.Repo("r");
        var prose = string.Join("\n", Enumerable.Range(1, 14).Select(i => $"Line {i} about the project and what it does."));
        t.File("r/CLAUDE.md", prose + "\nTBD\n");
        var codes = Codes(Quality_(t, "r"), "CLAUDE.md").ToList();
        Assert.Contains("claude-md-no-commands", codes);
        Assert.Contains("claude-md-no-structure", codes);
        Assert.Contains("claude-md-placeholder", codes);
        Assert.DoesNotContain("claude-md-thin", codes);
    }

    [Fact]
    public void A_very_long_claude_md_is_flagged()
    {
        using var t = new FixtureTree();
        t.Repo("r");
        var body = GoodClaudeMd + "\n" + string.Join("\n", Enumerable.Range(1, 320).Select(i => $"- rule {i}"));
        t.File("r/CLAUDE.md", body);
        Assert.Contains("claude-md-too-long", Codes(Quality_(t, "r"), "CLAUDE.md"));
    }

    [Fact]
    public void Claude_md_references_to_missing_files_are_reported_and_real_ones_are_not()
    {
        using var t = new FixtureTree();
        t.Repo("r");
        t.File("r/docs/guide.md", "guide");
        t.File("r/.claude/skills/x/SKILL.md", GoodSkill("x"));
        var text = GoodClaudeMd + "\nSee @docs/guide.md and @docs/gone.md, [api](docs/api.md), [site](https://example.com/x.md), `.claude/skills/x/SKILL.md`, `.claude/agents/missing.md`.\n"
            + "Install with npm i @angular/core and ask @someone. Mail me@example.com. See ~/notes.md and `docs/other.md` in a span.\n"
            + "```\n@docs/in-a-fence.md\n```\n";
        t.File("r/CLAUDE.md", text);

        var f = Assert.Single(Quality_(t, "r").Findings, x => x.Code == "claude-md-broken-ref");
        Assert.Contains("docs/gone.md", f.Detail);
        Assert.Contains("docs/api.md", f.Detail);
        Assert.Contains(".claude/agents/missing.md", f.Detail);
        Assert.DoesNotContain("guide.md", f.Detail);
        Assert.DoesNotContain("angular", f.Detail);
        Assert.DoesNotContain("in-a-fence", f.Detail);
        Assert.DoesNotContain("example.com", f.Detail);
    }

    [Fact]
    public void Agents_are_checked_for_frontmatter_description_tools_and_prompt()
    {
        using var t = new FixtureTree();
        t.Repo("r");
        t.File("r/.claude/agents/none.md", "just text");
        t.File("r/.claude/agents/vague.md", "---\nname: vague\ndescription: Helps\n---\nDo it.\n");
        t.File("r/.claude/agents/odd-name.md", GoodAgent("other-name"));
        var q = Quality_(t, "r");

        Assert.Contains("agent-no-frontmatter", Codes(q, ".claude/agents/none.md"));
        var vague = Codes(q, ".claude/agents/vague.md").ToList();
        Assert.Contains("agent-short-description", vague);
        Assert.Contains("agent-no-tools", vague);
        Assert.Contains("agent-thin-prompt", vague);
        Assert.Equal(["agent-name-mismatch"], Codes(q, ".claude/agents/odd-name.md"));
    }

    [Fact]
    public void An_agent_without_a_description_is_an_error()
    {
        using var t = new FixtureTree();
        t.Repo("r");
        t.File("r/.claude/agents/a.md", "---\nname: a\ntools: Read\n---\nOne.\nTwo.\nThree.\nFour.\nFive.\n");
        var f = Assert.Single(Quality_(t, "r").Findings, x => x.Code == "agent-no-description");
        Assert.Equal(Severities.Error, f.Severity);
    }

    [Fact]
    public void Agents_with_the_same_name_are_reported_once_on_the_later_file()
    {
        using var t = new FixtureTree();
        t.Repo("r");
        t.File("r/.claude/agents/a.md", GoodAgent("twin"));
        t.File("r/.claude/agents/b.md", GoodAgent("twin"));
        var f = Assert.Single(Quality_(t, "r").Findings, x => x.Code == "agent-duplicate-name");
        Assert.Equal(".claude/agents/b.md", f.Path);
    }

    [Fact]
    public void Skills_need_a_description_that_says_when_to_use_them()
    {
        using var t = new FixtureTree();
        t.Repo("r");
        t.File("r/.claude/skills/none/SKILL.md", "---\nname: none\n---\nOne.\nTwo.\nThree.\nFour.\nFive.\n");
        t.File("r/.claude/skills/short/SKILL.md", "---\nname: short\ndescription: EF\n---\n");
        t.File("r/.claude/skills/renamed/SKILL.md", GoodSkill("something-else"));
        var q = Quality_(t, "r");

        Assert.Contains("skill-no-description", Codes(q, ".claude/skills/none/SKILL.md"));
        var short_ = Codes(q, ".claude/skills/short/SKILL.md").ToList();
        Assert.Contains("skill-short-description", short_);
        Assert.Contains("skill-thin-body", short_);
        Assert.Equal(["skill-name-mismatch"], Codes(q, ".claude/skills/renamed/SKILL.md"));
    }

    [Fact]
    public void Scores_subtract_penalties_and_never_go_below_zero()
    {
        Assert.Equal(100, Quality.ScoreOf([]));
        Assert.Equal(85, Quality.ScoreOf([new("p", "c", Severities.Warning)]));
        Assert.Equal(60, Quality.ScoreOf([new("p", "c", Severities.Error), new("p", "c", Severities.Info)]));
        Assert.Equal(0, Quality.ScoreOf(Enumerable.Repeat(new QualityFinding("p", "c", Severities.Error), 4)));
    }

    [Fact]
    public void Frontmatter_body_skips_the_header_block()
    {
        Assert.Equal("body", Frontmatter.BodyOf("---\nname: a\n---\nbody"));
        Assert.Equal("no header", Frontmatter.BodyOf("no header"));
        Assert.Equal("---\nopen", Frontmatter.BodyOf("---\nopen"));
    }
}
