using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace Radar.Server.Tests;

public class FileEndpointTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "radar-files-" + Guid.NewGuid().ToString("N"));
    private readonly string _outside = Path.Combine(Path.GetTempPath(), "radar-outside-" + Guid.NewGuid().ToString("N"));
    private readonly ServerFactory _f = new();
    private HttpClient _c = null!;

    private void W(string rel, string content)
    {
        var p = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
    }

    private async Task ScanAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "orders-api", ".git"));
        W("orders-api/CLAUDE.md", "# Orders\n\nSee .claude/workflows/commit.md");
        W("orders-api/.claude/agents/cr.md", "---\nname: cr\ndescription: Reviews\n---\nBody of the agent");
        W("orders-api/.claude/skills/ef/SKILL.md", "---\nname: ef\n---\nSkill body");
        W("orders-api/.claude/workflows/commit.md", "---\nname: commit\n---\nWorkflow body");
        W("orders-api/.env", "SECRET=1");
        W("orders-api/notes.txt", "not markdown");
        W("orders-api/.claude/agents/big.md", "---\nname: big\n---\n" + new string('x', 300 * 1024));

        Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_outside, "stolen.md"), "TOP SECRET");
        if (!OperatingSystem.IsWindows())
        {
            File.CreateSymbolicLink(Path.Combine(_root, "orders-api", ".claude", "agents", "linked.md"), Path.Combine(_outside, "stolen.md"));
        }

        _c = await _f.AuthedClientAsync();
        await _c.PutAsJsonAsync("/api/settings", new { scanPath = _root });
        var start = await _c.PostAsync("/api/scans", null);
        var id = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        using var resp = await _c.GetAsync($"/api/scans/{id}/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (await reader.ReadLineAsync(cts.Token) is not null) { }
    }

    private Task<HttpResponseMessage> Get(string repo, string path) =>
        _c.GetAsync($"/api/file?repo={Uri.EscapeDataString(repo)}&path={Uri.EscapeDataString(path)}");

    [Fact]
    public async Task Returns_the_content_of_files_reported_by_the_scan()
    {
        await ScanAsync();
        foreach (var (path, needle) in new[]
        {
            ("CLAUDE.md", "# Orders"),
            (".claude/agents/cr.md", "Body of the agent"),
            (".claude/skills/ef/SKILL.md", "Skill body"),
            (".claude/workflows/commit.md", "Workflow body")
        })
        {
            var r = await Get("orders-api", path);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var body = await r.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(needle, body.GetProperty("content").GetString());
            Assert.False(body.GetProperty("truncated").GetBoolean());
        }
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData(".env")]
    [InlineData("notes.txt")]
    [InlineData(".claude/agents/../../.env")]
    [InlineData(".claude/agents/missing.md")]
    [InlineData("CLAUDE.MD")]
    public async Task Refuses_anything_the_scan_did_not_report(string path)
    {
        await ScanAsync();
        var r = await Get("orders-api", path);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.DoesNotContain("SECRET", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Refuses_unknown_repos_and_repo_traversal()
    {
        await ScanAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Get("nope", "CLAUDE.md")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Get("../orders-api", "CLAUDE.md")).StatusCode);
    }

    [Fact]
    public async Task Does_not_follow_a_symlink_out_of_the_repo()
    {
        if (OperatingSystem.IsWindows()) return;
        await ScanAsync();
        var r = await Get("orders-api", ".claude/agents/linked.md");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.DoesNotContain("TOP SECRET", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Truncates_large_files()
    {
        await ScanAsync();
        var body = await (await Get("orders-api", ".claude/agents/big.md")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("truncated").GetBoolean());
        Assert.True(body.GetProperty("content").GetString()!.Length <= 256 * 1024);
        Assert.True(body.GetProperty("bytes").GetInt64() > 256 * 1024);
    }

    [Fact]
    public async Task Needs_a_scan_and_a_token()
    {
        _c = await _f.AuthedClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Get("orders-api", "CLAUDE.md")).StatusCode);
        var anon = _f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/file?repo=a&path=CLAUDE.md")).StatusCode);
    }

    public void Dispose()
    {
        _f.Dispose();
        foreach (var d in new[] { _root, _outside }) { try { Directory.Delete(d, true); } catch { } }
    }
}
