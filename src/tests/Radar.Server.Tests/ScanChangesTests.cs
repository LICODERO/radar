using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Radar.Scanner;
using Radar.Server.Infrastructure.Storage;

namespace Radar.Server.Tests;

public class ScanChangesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "radar-changes-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    private static RepoInfo Repo(string id, int coverage, int quality, params QualityFinding[] findings) =>
        new(id, id, id, "XX", ".NET", [".NET"], new ClaudeMdInfo(true, "CLAUDE.md"), [], [], new CoverageInfo(coverage, new CoverageParts(40, 0, 0)), [],
            new QualityInfo(quality, [], findings));

    private static ScanResult Scan(DateTimeOffset at, string root, params RepoInfo[] repos) =>
        new(ScanResult.CurrentSchemaVersion, root, at, 0, new SummaryInfo(repos.Length, 0, 0, 0, 0, [], 50, 50), repos, [], [], []);

    [Fact]
    public async Task A_rescan_that_changes_nothing_keeps_the_older_baseline()
    {
        var store = new JsonFileStore(_dir);
        await store.SaveAsync(Scan(T0, "/p", Repo("r", 40, 100)));
        Assert.Null(await store.LoadPreviousAsync());                       // nothing to compare with yet

        await store.SaveAsync(Scan(T0.AddHours(1), "/p", Repo("r", 70, 80)));
        var prev = await store.LoadPreviousAsync();
        Assert.Equal(T0, prev!.ScannedAt);                                  // the first scan became the baseline

        await store.SaveAsync(Scan(T0.AddHours(2), "/p", Repo("r", 70, 80))); // same content, new time
        await store.SaveAsync(Scan(T0.AddHours(3), "/p", Repo("r", 70, 80)));
        Assert.Equal(T0, (await store.LoadPreviousAsync())!.ScannedAt);     // still the first one, not the last quick rescan
    }

    [Fact]
    public async Task Changing_the_scanned_directory_drops_the_history()
    {
        var store = new JsonFileStore(_dir);
        await store.SaveAsync(Scan(T0, "/p", Repo("r", 40, 100)));
        await store.SaveAsync(Scan(T0.AddHours(1), "/p", Repo("r", 70, 80)));
        Assert.NotNull(await store.LoadPreviousAsync());

        await store.SaveAsync(Scan(T0.AddHours(2), "/other", Repo("r", 70, 80)));
        Assert.Null(await store.LoadPreviousAsync());
    }

    [Fact]
    public async Task The_changes_endpoint_is_204_without_history_and_describes_the_diff_with_it()
    {
        using var f = new ServerFactory();
        var c = await f.AuthedClientAsync();
        Directory.CreateDirectory(f.DataDir);
        Write(f, "scan-result.json", Scan(T0.AddDays(1), "/p", Repo("r", 100, 90)));
        Assert.Equal(HttpStatusCode.NoContent, (await c.GetAsync("/api/scan/changes")).StatusCode);

        Write(f, "scan-previous.json", Scan(T0, "/p", Repo("r", 70, 60, new QualityFinding("CLAUDE.md", "claude-md-thin", Severities.Warning, "9"))));
        var changes = await c.GetFromJsonAsync<JsonElement>("/api/scan/changes");
        Assert.Equal(1, changes.GetProperty("changed").GetArrayLength());
        Assert.Equal("claude-md-thin", changes.GetProperty("fixed")[0].GetProperty("code").GetString());
        Assert.Equal(50, changes.GetProperty("avgCoverageBefore").GetInt32());   // the averages are the summaries' (50 in both fixtures)
    }

    private static void Write(ServerFactory f, string name, ScanResult r) =>
        File.WriteAllBytes(Path.Combine(f.DataDir, name), JsonSerializer.SerializeToUtf8Bytes(r, RadarJson.Options));
}
