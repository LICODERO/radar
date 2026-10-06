using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Radar.Server.Tests;

public class NewAgentVisibilityTests : IDisposable
{
    private const string Reviewer = "---\nname: reviewer\ndescription: Reviews pull requests for correctness and style\ntools: Read\n---\n\nYou review code.\nRead the diff first.\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "radar-newvis-" + Guid.NewGuid().ToString("N"));
    private readonly ServerFactory _f = new();
    private HttpClient _c = null!;

    public void Dispose() { _f.Dispose(); try { Directory.Delete(_root, true); } catch { /* best effort */ } }

    private string RepoDir(string r) => Path.Combine(_root, r);
    private string AgentFile(string r) => Path.Combine(RepoDir(r), ".claude", "agents", "reviewer.md");
    private string Exclude(string r) => Path.Combine(RepoDir(r), ".git", "info", "exclude");

    private static string Git(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-C", dir }.Concat(args)) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return o;
    }

    /// <summary>real-a (has the agent), real-b (real git), fake-c (an empty .git folder: git cannot hide anything there).</summary>
    private async Task StartAsync()
    {
        foreach (var r in new[] { "real-a", "real-b" }) { Directory.CreateDirectory(RepoDir(r)); Git(RepoDir(r), "init", "-q"); }
        Directory.CreateDirectory(Path.Combine(RepoDir("fake-c"), ".git"));
        Directory.CreateDirectory(Path.GetDirectoryName(AgentFile("real-a"))!);
        File.WriteAllText(AgentFile("real-a"), Reviewer);

        _c = await _f.AuthedClientAsync();
        await _c.PutAsJsonAsync("/api/settings", new { scanPath = _root });
        var id = (await (await _c.PostAsync("/api/scans", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        using var resp = await _c.GetAsync($"/api/scans/{id}/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (await reader.ReadLineAsync(cts.Token) is not null) { }
    }

    private static string Status(JsonElement body, string repo) =>
        body.GetProperty("targets").EnumerateArray().Single(t => t.GetProperty("repoId").GetString() == repo).GetProperty("status").GetString()!;

    [Fact]
    public async Task A_new_private_agent_is_created_and_hidden_from_git()
    {
        await StartAsync();
        var r = await _c.PostAsJsonAsync("/api/agents", new { repoId = "real-b", content = Reviewer, visibility = "private" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.True((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hidden").GetBoolean());
        Assert.True(File.Exists(AgentFile("real-b")));
        Assert.Contains("/.claude/agents/reviewer.md", File.ReadAllText(Exclude("real-b")));
        Assert.Equal("", Git(RepoDir("real-b"), "status", "--porcelain").Trim());
    }

    [Fact]
    public async Task Without_a_visibility_the_file_is_left_as_git_sees_it()
    {
        await StartAsync();
        var r = await _c.PostAsJsonAsync("/api/agents", new { repoId = "real-b", content = Reviewer });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.DoesNotContain("radar:private", File.ReadAllText(Exclude("real-b")));
        Assert.Contains("reviewer.md", Git(RepoDir("real-b"), "status", "--porcelain", "-uall"));
    }

    [Fact]
    public async Task A_private_agent_is_not_created_when_git_cannot_hide_it()
    {
        await StartAsync();
        var r = await _c.PostAsJsonAsync("/api/agents", new { repoId = "fake-c", content = Reviewer, visibility = "private" });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("no-git", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("blocked").GetString());
        Assert.False(File.Exists(AgentFile("fake-c")));

        var pub = await _c.PostAsJsonAsync("/api/agents", new { repoId = "fake-c", content = Reviewer, visibility = "public" });
        Assert.Equal(HttpStatusCode.Created, pub.StatusCode);
    }

    [Fact]
    public async Task A_damaged_block_stops_a_private_agent_before_anything_is_written()
    {
        await StartAsync();
        File.AppendAllText(Exclude("real-b"), "# >>> radar:private >>>\n/x\n");
        var r = await _c.PostAsJsonAsync("/api/agents", new { repoId = "real-b", content = Reviewer, visibility = "private" });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.False(File.Exists(AgentFile("real-b")));
    }

    [Fact]
    public async Task A_private_copy_skips_the_repos_where_it_cannot_be_hidden()
    {
        await StartAsync();
        var plan = await (await _c.PostAsJsonAsync("/api/agents/copy", new { fromRepoId = "real-a", name = "reviewer", toRepoIds = new[] { "real-b", "fake-c" }, visibility = "private" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ready", Status(plan, "real-b"));
        Assert.Equal("cannot-hide", Status(plan, "fake-c"));
        Assert.False(File.Exists(AgentFile("real-b")));

        var done = await (await _c.PostAsJsonAsync("/api/agents/copy", new { fromRepoId = "real-a", name = "reviewer", toRepoIds = new[] { "real-b", "fake-c" }, visibility = "private", confirm = true })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("created", Status(done, "real-b"));
        Assert.Equal("cannot-hide", Status(done, "fake-c"));
        Assert.True(File.Exists(AgentFile("real-b")));
        Assert.False(File.Exists(AgentFile("fake-c")));
        Assert.Contains("/.claude/agents/reviewer.md", File.ReadAllText(Exclude("real-b")));
        Assert.Equal("", Git(RepoDir("real-b"), "status", "--porcelain").Trim());
    }
}
