using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Radar.Server.Tests;

public class AgentCopyTests : IDisposable
{
    private const string Reviewer = "---\nname: reviewer\ndescription: Reviews pull requests for correctness and style\ntools: Read, Grep\n---\n\nYou review code.\nRead the diff first.\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "radar-copy-" + Guid.NewGuid().ToString("N"));
    private readonly ServerFactory _f = new();
    private HttpClient _c = null!;

    public void Dispose() { _f.Dispose(); try { Directory.Delete(_root, true); } catch { /* best effort */ } }

    private string AgentFile(string repo) => Path.Combine(_root, repo, ".claude", "agents", "reviewer.md");

    private async Task StartAsync(string sourceContent = Reviewer)
    {
        foreach (var r in new[] { "a-api", "b-api", "c-api" }) Directory.CreateDirectory(Path.Combine(_root, r, ".git"));
        Directory.CreateDirectory(Path.GetDirectoryName(AgentFile("a-api"))!);
        File.WriteAllText(AgentFile("a-api"), sourceContent);
        Directory.CreateDirectory(Path.GetDirectoryName(AgentFile("c-api"))!);
        File.WriteAllText(AgentFile("c-api"), "---\nname: reviewer\ndescription: Own version of the reviewer agent\n---\nOwn.\n");

        _c = await _f.AuthedClientAsync();
        await _c.PutAsJsonAsync("/api/settings", new { scanPath = _root });
        var id = (await (await _c.PostAsync("/api/scans", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        using var resp = await _c.GetAsync($"/api/scans/{id}/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (await reader.ReadLineAsync(cts.Token) is not null) { }
    }

    private Task<HttpResponseMessage> Copy(object body) => _c.PostAsJsonAsync("/api/agents/copy", body);

    private static string Status(JsonElement body, string repo) =>
        body.GetProperty("targets").EnumerateArray().Single(t => t.GetProperty("repoId").GetString() == repo).GetProperty("status").GetString()!;

    [Fact]
    public async Task Without_confirm_it_only_reports_the_plan_and_writes_nothing()
    {
        await StartAsync();
        var r = await Copy(new { fromRepoId = "a-api", name = "reviewer", toRepoIds = new[] { "b-api", "c-api" } });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("written").GetBoolean());
        Assert.Equal("ready", Status(body, "b-api"));
        Assert.Equal("exists", Status(body, "c-api"));
        Assert.False(File.Exists(AgentFile("b-api")));
    }

    [Fact]
    public async Task With_confirm_it_creates_the_file_and_never_overwrites_an_existing_one()
    {
        await StartAsync();
        var before = File.ReadAllText(AgentFile("c-api"));
        var r = await Copy(new { fromRepoId = "a-api", name = "Reviewer", toRepoIds = new[] { "b-api", "c-api" }, confirm = true });
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("created", Status(body, "b-api"));
        Assert.Equal("exists", Status(body, "c-api"));
        Assert.Equal(Reviewer.TrimEnd() + "\n", File.ReadAllText(AgentFile("b-api")));
        Assert.Equal(before, File.ReadAllText(AgentFile("c-api")));
    }

    [Fact]
    public async Task The_source_repo_and_unknown_repos_are_not_written()
    {
        await StartAsync();
        var r = await Copy(new { fromRepoId = "a-api", name = "reviewer", toRepoIds = new[] { "a-api", "../escape", "nope" }, confirm = true });
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("exists", Status(body, "a-api"));
        Assert.Equal("unknown-repo", Status(body, "../escape"));
        Assert.Equal("unknown-repo", Status(body, "nope"));
        Assert.False(Directory.Exists(Path.Combine(_root, "..", "escape")));
    }

    [Fact]
    public async Task Bad_requests_are_refused()
    {
        await StartAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Copy(new { fromRepoId = "a-api", name = "reviewer", toRepoIds = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Copy(new { fromRepoId = "a-api", name = "reviewer", toRepoIds = Enumerable.Range(0, 51).Select(i => "r" + i).ToArray() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Copy(new { fromRepoId = "nope", name = "reviewer", toRepoIds = new[] { "b-api" } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Copy(new { fromRepoId = "a-api", name = "ghost", toRepoIds = new[] { "b-api" } })).StatusCode);
    }

    [Fact]
    public async Task A_source_agent_that_is_itself_invalid_is_not_copied()
    {
        await StartAsync("---\nname: reviewer\n---\nBody\n");
        var r = await Copy(new { fromRepoId = "a-api", name = "reviewer", toRepoIds = new[] { "b-api" }, confirm = true });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.False(File.Exists(AgentFile("b-api")));
    }

    [Fact]
    public async Task It_needs_the_session_token()
    {
        await StartAsync();
        using var anon = _f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/agents/copy", new { fromRepoId = "a-api", name = "reviewer", toRepoIds = new[] { "b-api" }, confirm = true })).StatusCode);
    }
}
