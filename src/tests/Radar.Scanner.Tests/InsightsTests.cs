using Radar.Scanner;

namespace Radar.Scanner.Tests;

public class InsightsTests
{
    // ---- staleness --------------------------------------------------------------------------------

    [Fact]
    public void Claude_md_that_trails_the_repo_by_half_a_year_is_stale()
    {
        var activity = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var f = Quality.Stale("CLAUDE.md", activity.AddDays(-200), activity);
        Assert.NotNull(f);
        Assert.Equal("claude-md-stale", f!.Code);
        Assert.Equal("200", f.Detail);
        Assert.Null(Quality.Stale("CLAUDE.md", activity.AddDays(-30), activity));
        Assert.Null(Quality.Stale("CLAUDE.md", null, activity));
        Assert.Null(Quality.Stale("CLAUDE.md", activity.AddDays(-400), null));
    }

    [Fact]
    public void The_analyzer_reads_both_times_from_disk()
    {
        using var t = new FixtureTree();
        t.Repo("r");
        var claude = t.File("r/CLAUDE.md", CompleteClaudeMd);
        var log = t.File("r/.git/logs/HEAD", "x");
        System.IO.File.SetLastWriteTimeUtc(claude, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        System.IO.File.SetLastWriteTimeUtc(log, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var q = RepoAnalyzer.Analyze(t.Root, t.Path_("r")).Repo.Quality!;
        Assert.Contains(q.Findings, f => f.Code == "claude-md-stale");
    }

    private static string CompleteClaudeMd => QualityTests.GoodClaudeMd;

    // ---- hashes and shared items ------------------------------------------------------------------

    [Fact]
    public void Hash_ignores_line_endings_and_outer_blanks_but_not_content()
    {
        Assert.Equal(ContentHash.Of("a\nb"), ContentHash.Of("\r\na\r\nb\r\n"));
        Assert.NotEqual(ContentHash.Of("a\nb"), ContentHash.Of("a\nc"));
        Assert.Null(ContentHash.Of(null));
        Assert.Equal(12, ContentHash.Of("x")!.Length);
    }

    private static RepoInfo Repo(string id, string stack, params AgentInfo[] agents) =>
        new(id, id, id, id[..2].ToUpperInvariant(), stack, [stack], new ClaudeMdInfo(true, "CLAUDE.md"), agents, [], new CoverageInfo(70, new CoverageParts(40, 30, 0)), []);

    private static AgentInfo Agent(string name, string hash) => new(name, "d", [], null, $".claude/agents/{name}.md", hash);

    [Fact]
    public void Shared_items_group_by_name_split_by_content_and_list_who_lacks_them()
    {
        var repos = new[]
        {
            Repo("a-api", ".NET", Agent("reviewer", "h1"), Agent("solo", "s")),
            Repo("b-api", ".NET", Agent("Reviewer", "h2")),
            Repo("c-api", ".NET", Agent("reviewer", "h1")),
            Repo("d-api", ".NET"),
            Repo("e-front", "Angular")
        };

        var shared = SharedItems.Compute(repos);

        var r = Assert.Single(shared);
        Assert.Equal("agent", r.Kind);
        Assert.Equal(2, r.Variants.Count);
        Assert.Equal(["a-api", "c-api"], r.Variants[0].Repos);   // the most common copy first
        Assert.Equal(["b-api"], r.Variants[1].Repos);
        Assert.Equal(["d-api"], r.Missing);                       // same stack as a holder; the Angular repo is not asked to have it
    }

    [Fact]
    public void Identical_copies_are_one_variant_and_listed_after_drifted_ones()
    {
        var repos = new[]
        {
            Repo("a-api", ".NET", Agent("same", "x"), Agent("drift", "1")),
            Repo("b-api", ".NET", Agent("same", "x"), Agent("drift", "2"))
        };
        var shared = SharedItems.Compute(repos);
        Assert.Equal(["drift", "same"], shared.Select(s => s.Name));
        Assert.Single(shared[1].Variants);
    }

    [Fact]
    public void The_scanner_fills_shared_items_from_real_files()
    {
        using var t = new FixtureTree();
        t.Repo("a"); t.Repo("b");
        t.File("a/.claude/agents/rev.md", QualityTests.GoodAgent("rev"));
        t.File("b/.claude/agents/rev.md", QualityTests.GoodAgent("rev"));
        var r = new RadarScanner().Scan(new ScanOptions(t.Root));
        var s = Assert.Single(r.Shared!);
        Assert.Single(s.Variants);
        Assert.Equal(["a", "b"], s.Variants[0].Repos);
    }
}
