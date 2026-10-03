using Radar.Scanner;

namespace Radar.Scanner.Tests;

public class ScannerTests
{
    private sealed class Collector : IProgress<ScanEvent>
    {
        public List<ScanEvent> Events { get; } = [];
        public void Report(ScanEvent value) => Events.Add(value);
    }

    private static FixtureTree BuildProjects()
    {
        var t = new FixtureTree();
        t.Repo("orders-api"); t.File("orders-api/CLAUDE.md", "x"); t.File("orders-api/App.csproj");
        t.File("orders-api/.claude/agents/code-reviewer.md", "---\nname: code-reviewer\n---\n");
        t.File("orders-api/.claude/skills/ef/SKILL.md", "---\nname: ef\n---\n");
        t.File("orders-api/.claude/workflows/commit.md", "---\nname: commit\ndescription: Commit\nagents: [code-reviewer, ghost]\nskills: [ef, phantom]\n---\n");
        t.Repo("orders-front"); t.File("orders-front/CLAUDE.md", "See .claude/workflows/commit.md"); t.File("orders-front/angular.json");
        t.File("orders-front/.claude/workflows/commit.md", "---\nname: commit\n---\n");
        t.Repo("billing-api");
        t.Dir("not-a-repo");
        return t;
    }

    [Fact]
    public void Builds_a_consistent_result()
    {
        using var t = BuildProjects();
        var r = new RadarScanner().Scan(new ScanOptions(t.Root));

        Assert.Equal(3, r.Summary.Repos);
        Assert.Equal(1, r.Summary.Agents);
        Assert.Equal(1, r.Summary.Skills);
        Assert.Equal(1, r.Summary.Workflows);
        Assert.Equal(1, r.Summary.Gaps);
        Assert.Equal("billing-api", Assert.Single(r.Gaps).RepoId);
        Assert.Equal(["billing-api", "orders-api", "orders-front"], r.Repos.Select(x => x.Id));
        Assert.Equal(ScanResult.CurrentSchemaVersion, r.SchemaVersion);
        Assert.Contains(".NET", r.Summary.Stacks);
        Assert.Contains("Angular", r.Summary.Stacks);
        var avg = (int)Math.Round(r.Repos.Average(x => x.Coverage.Score));
        Assert.Equal(avg, r.Summary.AvgCoverage);
    }

    [Fact]
    public void Aggregates_workflows_by_name_and_flags_missing_agents()
    {
        using var t = BuildProjects();
        var r = new RadarScanner().Scan(new ScanOptions(t.Root));

        var wf = Assert.Single(r.Workflows);
        Assert.Equal("W1", wf.Id);
        Assert.Equal("commit", wf.Name);
        Assert.Equal(2, wf.Repos.Count);
        Assert.Equal("Commit", wf.Description);
        Assert.Equal(["code-reviewer", "ghost"], wf.Agents);
        Assert.Contains(wf.Issues, i => i.Contains("ghost"));
        Assert.Equal(["ef", "phantom"], wf.Skills);
        // every repo keeps what its own copy names: the workflow only relates to that repo's elements
        Assert.Equal(["code-reviewer", "ghost"], wf.Repos.Single(r => r.RepoId == "orders-api").Agents);
        Assert.Empty(wf.Repos.Single(r => r.RepoId == "orders-front").Agents);
        Assert.Equal(["ef", "phantom"], wf.Repos.Single(r => r.RepoId == "orders-api").Skills);
        Assert.Contains(wf.Issues, i => i.Contains("phantom"));
        Assert.DoesNotContain(wf.Issues, i => i.Contains("'ef'"));
        Assert.False(wf.Repos.Single(x => x.RepoId == "orders-api").Linked);
        Assert.True(wf.Repos.Single(x => x.RepoId == "orders-front").Linked);
    }

    [Fact]
    public void Emits_real_progress_events_in_order()
    {
        using var t = BuildProjects();
        var c = new Collector();
        new RadarScanner().Scan(new ScanOptions(t.Root), c);

        Assert.IsType<ScanStarted>(c.Events[0]);
        var phases = c.Events.OfType<ScanPhase>().Select(p => p.Phase).ToList();
        Assert.Equal(["init", "discovery", "analysis", "linking", "done"], phases);
        Assert.Equal(3, c.Events.OfType<RepoFound>().Count());

        var scanned = c.Events.OfType<RepoScanned>().ToList();
        Assert.Equal(3, scanned.Count);
        Assert.Equal([1, 2, 3], scanned.Select(s => s.Index));
        Assert.All(scanned, s => Assert.Equal(3, s.Total));
        Assert.Equal(90, scanned[^1].Percent);
        Assert.Equal(3, scanned[^1].Counters.Repos);
        Assert.Equal(1, scanned[^1].Counters.Agents);
        Assert.Equal(1, scanned[^1].Counters.Gaps);
        Assert.Null(c.Events.OfType<ScanPhase>().First(p => p.Phase == "discovery").Percent);
        Assert.IsType<ScanCompleted>(c.Events[^1]);
    }

    [Fact]
    public void Cancellation_stops_the_scan()
    {
        using var t = BuildProjects();
        using var cts = new CancellationTokenSource();
        var progress = new Progress(e => { if (e is RepoScanned) cts.Cancel(); });
        Assert.Throws<OperationCanceledException>(() => new RadarScanner().Scan(new ScanOptions(t.Root), progress, cts.Token));
    }

    [Fact]
    public void Empty_directory_gives_an_empty_result()
    {
        using var t = new FixtureTree();
        var r = new RadarScanner().Scan(new ScanOptions(t.Root));
        Assert.Equal(0, r.Summary.Repos);
        Assert.Equal(0, r.Summary.AvgCoverage);
        Assert.Empty(r.Workflows);
    }

    [Fact]
    public void Missing_directory_throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() => new RadarScanner().Scan(new ScanOptions("/definitely/not/here-" + Guid.NewGuid())));
    }

    [Fact]
    public void Scanning_never_modifies_the_tree()
    {
        using var t = BuildProjects();
        string Snapshot() => string.Join("|", Directory.EnumerateFileSystemEntries(t.Root, "*", SearchOption.AllDirectories)
            .OrderBy(x => x).Select(x => x + File.GetLastWriteTimeUtc(x).Ticks));
        var before = Snapshot();
        new RadarScanner().Scan(new ScanOptions(t.Root));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Result_serializes_to_camel_case_json_matching_the_contract()
    {
        using var t = BuildProjects();
        var r = new RadarScanner().Scan(new ScanOptions(t.Root));
        var json = System.Text.Json.JsonSerializer.Serialize(r, RadarJson.Options);
        Assert.Contains("\"schemaVersion\":2", json);
        Assert.Contains("\"claudeMd\":{\"exists\":true", json);
        Assert.Contains("\"avgCoverage\"", json);
        Assert.Contains("\"quality\":{", json);
        Assert.Contains("\"repoId\"", json);
    }

    private sealed class Progress(Action<ScanEvent> on) : IProgress<ScanEvent>
    {
        public void Report(ScanEvent value) => on(value);
    }
}
