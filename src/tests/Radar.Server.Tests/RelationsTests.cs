using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Radar.Server.Tests;

public class RelationsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "radar-rel-" + Guid.NewGuid().ToString("N"));
    private readonly ServerFactory _f = new();
    private HttpClient _c = null!;

    public void Dispose() { _f.Dispose(); try { Directory.Delete(_root, true); } catch { /* best effort */ } }

    private string Dir(string repo) => Path.Combine(_root, repo);
    private string P(string repo, string rel) => Path.Combine(Dir(repo), rel.Replace('/', Path.DirectorySeparatorChar));
    private string Read(string repo, string rel) => File.ReadAllText(P(repo, rel));

    private static string Git(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-C", dir, "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false" }.Concat(args)) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return o;
    }

    private async Task StartAsync(bool claudeMdInApi = true)
    {
        foreach (var name in new[] { "web", "api" })
        {
            Directory.CreateDirectory(Dir(name));
            Git(Dir(name), "init", "-q");
            if (name == "web" || claudeMdInApi) File.WriteAllText(P(name, "CLAUDE.md"), "# " + name + "\n");
            else File.WriteAllText(P(name, "README.md"), "x");
            Git(Dir(name), "add", ".");
            Git(Dir(name), "commit", "-q", "-m", "init");
        }
        await ScanAsync();
    }

    private async Task ScanAsync()
    {
        _c = await _f.AuthedClientAsync();
        await _c.PutAsJsonAsync("/api/settings", new { scanPath = _root });
        var id = (await (await _c.PostAsync("/api/scans", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        using var resp = await _c.GetAsync($"/api/scans/{id}/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (await reader.ReadLineAsync(cts.Token) is not null) { }
    }

    private static object Rule(string visibility = "public", string auth = "oauth-client", string[]? env = null, string kind = "rest", string from = "web", string to = "api", string? notes = "tokens expire after 1h", string direction = "out") =>
        new { from, to, kind, auth, authEnv = env ?? ["API_CLIENT_ID", "API_CLIENT_SECRET"], contract = "openapi.yaml in api", notes, visibility, direction };

    private async Task<(HttpStatusCode Status, JsonElement Body)> Post(bool confirm, params object[] rules)
    {
        var r = await _c.PostAsJsonAsync("/api/relations", new { relations = rules, confirm });
        return (r.StatusCode, await r.Content.ReadFromJsonAsync<JsonElement>());
    }

    private async Task<JsonElement> Load() => await (await _c.GetAsync("/api/relations")).Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task A_public_rule_is_planned_first_then_written_to_both_repos_with_a_pointer_in_each_claude_md()
    {
        await StartAsync();
        var (status, plan) = await Post(false, Rule());
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(File.Exists(P("web", ".claude/relations.md")));
        Assert.Equal(2, plan.GetProperty("repos").GetArrayLength());

        var (_, done) = await Post(true, Rule());
        Assert.True(done.GetProperty("applied").GetBoolean());
        foreach (var repo in new[] { "web", "api" })
        {
            var rules = Read(repo, ".claude/relations.md");
            Assert.Contains("OAuth client credentials", rules);
            Assert.Contains("`API_CLIENT_SECRET`", rules);
            Assert.DoesNotContain("\"key\"", rules);                  // only what the user entered is stored
            var pointer = Read(repo, "CLAUDE.md");
            Assert.Contains("<!-- radar:relations-ref -->", pointer);
            Assert.Contains("`.claude/relations.md`", pointer);
        }
        Assert.Contains("`api` (calls it)", Read("web", "CLAUDE.md"));
        Assert.Contains("`web` (calls this repo)", Read("api", "CLAUDE.md"));
        Assert.False(File.Exists(P("web", "CLAUDE.local.md")));
    }

    [Fact]
    public async Task The_rules_can_be_read_back_from_the_files()
    {
        await StartAsync();
        await Post(true, Rule(), Rule(kind: "events", auth: "none", env: []));
        await ScanAsync();
        var graph = await Load();
        var rules = graph.GetProperty("relations").EnumerateArray().ToList();
        Assert.Equal(2, rules.Count);
        Assert.Contains(rules, r => r.GetProperty("kind").GetString() == "rest" && r.GetProperty("authEnv").GetArrayLength() == 2);
    }

    [Fact]
    public async Task A_private_rule_goes_to_the_local_files_and_never_to_git()
    {
        await StartAsync();
        var (_, done) = await Post(true, Rule("private"));
        Assert.True(done.GetProperty("applied").GetBoolean());
        Assert.Contains("radar:relations-ref", Read("web", "CLAUDE.local.md"));
        Assert.Contains("`.claude/relations.local.md`", Read("web", "CLAUDE.local.md"));
        Assert.False(File.Exists(P("web", ".claude/relations.md")));
        Assert.DoesNotContain("radar:relations", Read("web", "CLAUDE.md"));
        foreach (var repo in new[] { "web", "api" })
        {
            var status = Git(Dir(repo), "status", "--porcelain", "-uall");
            Assert.DoesNotContain("relations", status);
            Assert.DoesNotContain("CLAUDE.local.md", status);
        }
    }

    [Fact]
    public async Task A_second_run_changes_nothing_and_an_empty_set_removes_everything_RADAR_wrote()
    {
        await StartAsync();
        await Post(true, Rule(), Rule("private", kind: "events", auth: "none", env: []));
        var (_, again) = await Post(true, Rule(), Rule("private", kind: "events", auth: "none", env: []));
        Assert.Equal(0, again.GetProperty("repos").GetArrayLength());
        Assert.False(again.GetProperty("applied").GetBoolean());

        await ScanAsync();
        var (_, cleared) = await Post(true);
        Assert.True(cleared.GetProperty("applied").GetBoolean());
        foreach (var repo in new[] { "web", "api" })
        {
            Assert.False(File.Exists(P(repo, ".claude/relations.md")));
            Assert.False(File.Exists(P(repo, ".claude/relations.local.md")));
            Assert.False(File.Exists(P(repo, "CLAUDE.local.md")));
            Assert.Equal("# " + repo + "\n", Read(repo, "CLAUDE.md").Replace("\r\n", "\n"));
        }
    }

    [Fact]
    public async Task Text_the_user_wrote_around_the_blocks_is_kept()
    {
        await StartAsync();
        File.WriteAllText(P("web", "CLAUDE.md"), "# web\nkeep this\n");
        await Post(true, Rule());
        Assert.StartsWith("# web\nkeep this\n", Read("web", "CLAUDE.md"));
        await Post(true);
        Assert.Equal("# web\nkeep this\n", Read("web", "CLAUDE.md").Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task A_repo_without_claude_md_still_gets_the_file_and_a_note()
    {
        await StartAsync(claudeMdInApi: false);
        var (_, plan) = await Post(true, Rule());
        Assert.True(File.Exists(P("api", ".claude/relations.md")));
        Assert.False(File.Exists(P("api", "CLAUDE.md")));
        var api = plan.GetProperty("repos").EnumerateArray().First(r => r.GetProperty("repo").GetString() == "api");
        Assert.Contains(api.GetProperty("notes").EnumerateArray(), n => n.GetString() == "no-claude-md");
    }

    [Theory]
    [InlineData("secret-value=hunter2!")]            // a value, not a name
    [InlineData("with space")]
    public async Task Only_names_of_variables_are_accepted_never_values(string env)
    {
        await StartAsync();
        var (status, _) = await Post(true, Rule(env: [env]));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(File.Exists(P("web", ".claude/relations.md")));
    }

    [Fact]
    public async Task Bad_rules_are_refused_and_nothing_is_written()
    {
        await StartAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(true, Rule(kind: "carrier-pigeon"))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(true, Rule(auth: "none"))).Status);                    // env names with no auth
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(true, Rule(to: "web"))).Status);                       // a repo to itself
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(true, Rule(notes: "x --> y"))).Status);                // would break the block
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(true, Rule(), Rule())).Status);                        // duplicate
        Assert.Equal(HttpStatusCode.Conflict, (await Post(true, Rule(to: "ghost"))).Status);                       // unknown repo
        Assert.False(File.Exists(P("web", ".claude/relations.md")));
    }

    [Fact]
    public async Task Notes_cannot_break_the_json_fence()
    {
        await StartAsync();
        await Post(true, Rule(notes: "see ```code``` here\nsecond line"));
        await ScanAsync();
        var rules = (await Load()).GetProperty("relations");
        Assert.Equal("see 'code' here second line".Replace("''", "'"), rules[0].GetProperty("notes").GetString()!.Replace("'''", "'").Replace("''", "'"));
    }

    [Fact]
    public async Task Damaged_markers_block_everything_and_nothing_is_changed()
    {
        await StartAsync();
        File.WriteAllText(P("api", "CLAUDE.md"), "# api\n<!-- radar:relations-ref -->\nhalf a block\n");
        var (status, body) = await Post(true, Rule());
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("file-damaged", body.GetProperty("plan").GetProperty("blocked").GetString());
        Assert.False(File.Exists(P("web", ".claude/relations.md")));
        Assert.DoesNotContain("radar", Read("web", "CLAUDE.md"));
    }

    [Fact]
    public async Task A_private_rule_is_refused_when_the_local_file_is_already_tracked()
    {
        await StartAsync();
        File.WriteAllText(P("web", "CLAUDE.local.md"), "tracked by mistake\n");
        Git(Dir("web"), "add", "-f", "CLAUDE.local.md");
        Git(Dir("web"), "commit", "-q", "-m", "oops");
        var (status, body) = await Post(true, Rule("private"));
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("local-tracked", body.GetProperty("plan").GetProperty("blocked").GetString());
        Assert.False(File.Exists(P("api", ".claude/relations.local.md")));
    }

    [Fact]
    public async Task A_private_rule_hidden_in_the_public_file_is_ignored_on_load()
    {
        await StartAsync();
        await Post(true, Rule());
        var text = Read("web", ".claude/relations.md").Replace("\"visibility\":\"public\"", "\"visibility\":\"private\"");
        File.WriteAllText(P("web", ".claude/relations.md"), text);
        File.WriteAllText(P("api", ".claude/relations.md"), text);
        await ScanAsync();
        Assert.Equal(0, (await Load()).GetProperty("relations").GetArrayLength());
    }

    [Fact]
    public async Task A_repo_with_agents_md_and_no_claude_md_keeps_it_imported_when_the_local_file_is_created()
    {
        await StartAsync();
        File.Delete(P("web", "CLAUDE.md"));
        File.WriteAllText(P("web", "AGENTS.md"), "# agents\n");
        Git(Dir("web"), "add", "-A");
        Git(Dir("web"), "commit", "-q", "-m", "agents");
        await ScanAsync();
        await Post(true, Rule("private"));
        Assert.Contains("@AGENTS.md", Read("web", "CLAUDE.local.md"));
    }

    [Fact]
    public async Task The_direction_of_the_data_is_kept_for_events_and_forced_to_the_call_for_request_response_kinds()
    {
        await StartAsync();
        await Post(true, Rule(kind: "events", auth: "none", env: [], direction: "both"), Rule(kind: "rest", direction: "in"));
        Assert.Contains("- Data flow: `web` ↔ `api`", Read("web", ".claude/relations.md"));
        Assert.DoesNotContain("Data flow: `web` ←", Read("web", ".claude/relations.md"));   // rest says nothing: the call is the direction
        await ScanAsync();
        var rules = (await Load()).GetProperty("relations").EnumerateArray().ToDictionary(r => r.GetProperty("kind").GetString()!);
        Assert.Equal("both", rules["events"].GetProperty("direction").GetString());
        Assert.Equal("out", rules["rest"].GetProperty("direction").GetString());
    }

    [Fact]
    public async Task An_unknown_direction_is_refused_and_a_file_written_before_directions_existed_reads_as_out()
    {
        await StartAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(true, Rule(direction: "sideways"))).Status);
        await Post(true, Rule());
        var old = Read("web", ".claude/relations.md").Replace(",\"direction\":\"out\"", "");
        Assert.DoesNotContain("direction", old);
        File.WriteAllText(P("web", ".claude/relations.md"), old);
        await ScanAsync();
        Assert.Equal("out", (await Load()).GetProperty("relations")[0].GetProperty("direction").GetString());
    }
}
