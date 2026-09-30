using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Radar.Server.Features.Agents;
using Radar.Server.Features.Gaps;
using Radar.Server;

namespace Radar.Server.Tests;

public class AgentValidatorTests
{
    private const string Good = "---\nname: migration-reviewer\ndescription: Use when reviewing EF Core migrations.\ntools: Read, Grep\n---\n\nYou review migrations carefully and report risks.\n";

    [Fact]
    public void Accepts_a_well_formed_agent()
    {
        var v = AgentValidator.Validate(Good);
        Assert.True(v.Valid);
        Assert.Equal("migration-reviewer", v.Name);
    }

    [Fact]
    public void Normalize_strips_code_fences_and_chatter()
    {
        Assert.Equal(Good, AgentValidator.Normalize("```markdown\n" + Good + "```"));
        Assert.Equal(Good, AgentValidator.Normalize("Sure, here is the file:\n\n" + Good));
        Assert.Equal(Good, AgentValidator.Normalize(Good.Replace("\n", "\r\n")));
    }

    [Theory]
    [InlineData("name: x\n")]                                                       // no frontmatter
    [InlineData("---\nname: ok-name\ndescription: A long enough description.\n")]     // never closed
    public void Rejects_missing_or_broken_frontmatter(string content)
    {
        var v = AgentValidator.Validate(content);
        Assert.False(v.Valid);
        Assert.Null(v.Name);
    }

    [Theory]
    [InlineData("Bad Name")]
    [InlineData("UPPER")]
    [InlineData("../evil")]
    [InlineData("a")]
    [InlineData("-lead")]
    [InlineData("with_underscore")]
    public void Rejects_names_that_are_not_kebab_case(string name)
    {
        var v = AgentValidator.Validate($"---\nname: {name}\ndescription: A long enough description.\n---\nBody with enough text here.\n");
        Assert.False(v.Valid);
        Assert.Null(v.Name);
    }

    [Fact]
    public void Requires_a_description_and_a_body()
    {
        Assert.False(AgentValidator.Validate("---\nname: ok-name\n---\nBody with enough text here.\n").Valid);
        Assert.False(AgentValidator.Validate("---\nname: ok-name\ndescription: A long enough description.\n---\nshort\n").Valid);
    }

    [Fact]
    public void Rejects_huge_files()
    {
        var big = Good + new string('x', 70 * 1024);
        Assert.False(AgentValidator.Validate(big).Valid);
    }
}

public class AgentPromptTests
{
    [Fact]
    public void Contains_only_the_description_the_stack_and_existing_agent_names()
    {
        var p = AgentPrompt.Build("sprawdza migracje EF", ".NET", ["code-reviewer", "test-writer"]);
        Assert.Contains("sprawdza migracje EF", p);
        Assert.Contains(".NET", p);
        Assert.Contains("code-reviewer, test-writer", p);
        Assert.Contains("none", AgentPrompt.Build("x", "Angular", []));
    }
}

public class AgentCliTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "radar-fakeclaude-" + Guid.NewGuid().ToString("N"));

    private string FakeClaude(string body)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "claude");
        File.WriteAllText(path, "#!/bin/sh\n" + body);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static ClaudeCliGenerator Gen(string path) =>
        new(new PathToolLocator([]), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Radar:ClaudePath"] = path }).Build());

    [Fact]
    public async Task Runs_claude_without_tools_in_an_empty_temp_dir_and_sends_only_the_agreed_data()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = FakeClaude("pwd > \"$0.cwd\"\nprintf '%s\\n' \"$@\" > \"$0.args\"\ncat > \"$0.stdin\"\nprintf '%s' '{\"type\":\"result\",\"is_error\":false,\"result\":\"---\\nname: x\\n---\\nbody\",\"total_cost_usd\":0.0123}'\n");

        var r = await Gen(path).GenerateAsync(new AgentGenerationRequest("agent od migracji", ".NET", ["code-reviewer"]), CancellationToken.None);

        Assert.Equal("---\nname: x\n---\nbody", r.Text);
        Assert.Equal(0.0123m, r.CostUsd);

        var args = File.ReadAllLines(path + ".args");
        Assert.Contains("-p", args);
        var toolsAt = Array.IndexOf(args, "--tools");
        Assert.True(toolsAt >= 0);
        Assert.Equal("", args[toolsAt + 1]);                       // every tool disabled
        Assert.Contains("--no-session-persistence", args);
        Assert.Contains("--strict-mcp-config", args);              // no user MCP servers
        var sourcesAt = Array.IndexOf(args, "--setting-sources");
        Assert.True(sourcesAt >= 0);
        Assert.Equal("project", args[sourcesAt + 1]);              // no user-level settings sent along
        Assert.Contains("--max-budget-usd", args);

        var cwd = File.ReadAllText(path + ".cwd").Trim();
        Assert.Contains("radar-gen-", cwd);                        // an empty temp dir, never a repo
        Assert.False(Directory.Exists(cwd));                       // and it is cleaned up

        var stdin = File.ReadAllText(path + ".stdin");
        Assert.Contains("agent od migracji", stdin);
        Assert.Contains(".NET", stdin);
        Assert.Contains("code-reviewer", stdin);
    }

    [Fact]
    public async Task Reports_a_failing_claude_as_bad_gateway()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = FakeClaude("cat > /dev/null\necho 'not logged in' >&2\nexit 1\n");
        var e = await Assert.ThrowsAsync<GeneratorException>(() => Gen(path).GenerateAsync(new AgentGenerationRequest("x", ".NET", []), CancellationToken.None));
        Assert.Equal(502, e.Status);
        Assert.Contains("not logged in", e.Message);
    }

    [Fact]
    public async Task Reports_is_error_results()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = FakeClaude("cat > /dev/null\nprintf '%s' '{\"is_error\":true,\"result\":\"Credit balance too low\"}'\n");
        var e = await Assert.ThrowsAsync<GeneratorException>(() => Gen(path).GenerateAsync(new AgentGenerationRequest("x", ".NET", []), CancellationToken.None));
        Assert.Contains("Credit balance", e.Message);
    }

    [Fact]
    public async Task Reports_a_missing_claude_as_service_unavailable()
    {
        var gen = new ClaudeCliGenerator(new PathToolLocator([]), new ConfigurationBuilder().Build());
        var e = await Assert.ThrowsAsync<GeneratorException>(() => gen.GenerateAsync(new AgentGenerationRequest("x", ".NET", []), CancellationToken.None));
        Assert.Equal(503, e.Status);
    }

    [Fact]
    public async Task Cancelling_kills_the_process()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = FakeClaude("cat > /dev/null\nsleep 30\n");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var started = DateTime.UtcNow;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Gen(path).GenerateAsync(new AgentGenerationRequest("x", ".NET", []), cts.Token));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Parses_plain_text_and_array_outputs()
    {
        Assert.Equal("just text", ClaudeCliGenerator.Parse("just text").Text);
        Assert.Equal("done", ClaudeCliGenerator.Parse("[{\"type\":\"system\"},{\"type\":\"result\",\"result\":\"done\"}]").Text);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
}

public sealed class FakeGenerator(Func<AgentGenerationRequest, Task<GeneratedText>> impl) : IAgentGenerator
{
    public List<AgentGenerationRequest> Requests { get; } = [];
    public Task<GeneratedText> GenerateAsync(AgentGenerationRequest request, CancellationToken ct) { Requests.Add(request); return impl(request); }
}

public class AgentEndpointTests : IDisposable
{
    private const string Draft = "---\nname: migration-reviewer\ndescription: Use when reviewing EF Core migrations.\ntools: Read, Grep\n---\n\nYou review migrations carefully and report risks.\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "radar-agents-" + Guid.NewGuid().ToString("N"));
    private readonly string _outside = Path.Combine(Path.GetTempPath(), "radar-agents-out-" + Guid.NewGuid().ToString("N"));
    private FakeGenerator _gen = new(_ => Task.FromResult(new GeneratedText(Draft, 0.01m)));
    private Factory _f = null!;
    private HttpClient _c = null!;

    private sealed class Factory(IAgentGenerator gen) : ServerFactoryBase
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddSingleton(gen));
        }
    }

    private async Task StartAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "orders-api", ".git"));
        File.WriteAllText(Path.Combine(_root, "orders-api", "App.csproj"), "");
        Directory.CreateDirectory(Path.Combine(_root, "orders-api", ".claude", "agents"));
        File.WriteAllText(Path.Combine(_root, "orders-api", ".claude", "agents", "code-reviewer.md"), "---\nname: code-reviewer\n---\nBody");
        Directory.CreateDirectory(Path.Combine(_root, "plain", ".git"));

        _f = new Factory(_gen);
        _c = await _f.AuthedClientAsync();
        await _c.PutAsJsonAsync("/api/settings", new { scanPath = _root });
        var id = (await (await _c.PostAsync("/api/scans", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        using var resp = await _c.GetAsync($"/api/scans/{id}/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (await reader.ReadLineAsync(cts.Token) is not null) { }
    }

    private string AgentFile(string repo, string name) => Path.Combine(_root, repo, ".claude", "agents", name + ".md");

    [Fact]
    public async Task Generate_returns_a_validated_draft_and_writes_nothing()
    {
        await StartAsync();
        var r = await _c.PostAsJsonAsync("/api/agents/generate", new { repoId = "orders-api", description = "sprawdza migracje EF" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("valid").GetBoolean());
        Assert.Equal("migration-reviewer", body.GetProperty("name").GetString());
        Assert.Equal(".claude/agents/migration-reviewer.md", body.GetProperty("path").GetString());
        Assert.Equal(0.01m, body.GetProperty("costUsd").GetDecimal());
        Assert.False(File.Exists(AgentFile("orders-api", "migration-reviewer")));

        // only the agreed data goes to the generator
        var req = Assert.Single(_gen.Requests);
        Assert.Equal("sprawdza migracje EF", req.Description);
        Assert.Equal(".NET", req.Stack);
        Assert.Equal(["code-reviewer"], req.ExistingAgents);
    }

    [Fact]
    public async Task Generate_flags_an_invalid_draft_and_a_name_that_is_already_taken()
    {
        _gen = new FakeGenerator(_ => Task.FromResult(new GeneratedText("Sorry, I cannot do that.", null)));
        await StartAsync();
        var bad = await (await _c.PostAsJsonAsync("/api/agents/generate", new { repoId = "orders-api", description = "x" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(bad.GetProperty("valid").GetBoolean());
        Assert.NotEmpty(bad.GetProperty("errors").EnumerateArray());

        var taken = new FakeGenerator(_ => Task.FromResult(new GeneratedText(Draft.Replace("migration-reviewer", "code-reviewer"), null)));
        using var f2 = new Factory(taken);
        var c2 = await f2.AuthedClientAsync();
        await c2.PutAsJsonAsync("/api/settings", new { scanPath = _root });
        var id = (await (await c2.PostAsync("/api/scans", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        using (var resp = await c2.GetAsync($"/api/scans/{id}/events", HttpCompletionOption.ResponseHeadersRead))
        using (var reader = new StreamReader(await resp.Content.ReadAsStreamAsync()))
            while (await reader.ReadLineAsync() is not null) { }
        var t = await (await c2.PostAsJsonAsync("/api/agents/generate", new { repoId = "orders-api", description = "x" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(t.GetProperty("valid").GetBoolean());
        Assert.Contains(t.GetProperty("errors").EnumerateArray(), e => e.GetString()!.Contains("już istnieje"));
    }

    [Theory]
    [InlineData("nope", "opis", HttpStatusCode.NotFound)]
    [InlineData("orders-api", "", HttpStatusCode.BadRequest)]
    [InlineData("orders-api", "   ", HttpStatusCode.BadRequest)]
    public async Task Generate_validates_its_input(string repo, string description, HttpStatusCode expected)
    {
        await StartAsync();
        Assert.Equal(expected, (await _c.PostAsJsonAsync("/api/agents/generate", new { repoId = repo, description })).StatusCode);
        Assert.Empty(_gen.Requests);
    }

    [Fact]
    public async Task Generate_rejects_an_overlong_description()
    {
        await StartAsync();
        var r = await _c.PostAsJsonAsync("/api/agents/generate", new { repoId = "orders-api", description = new string('a', 2001) });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Generate_maps_generator_failures_and_allows_one_run_at_a_time()
    {
        var release = new TaskCompletionSource<GeneratedText>();
        _gen = new FakeGenerator(_ => release.Task);
        await StartAsync();

        var first = _c.PostAsJsonAsync("/api/agents/generate", new { repoId = "orders-api", description = "a" });
        await Task.Delay(200);
        var second = await _c.PostAsJsonAsync("/api/agents/generate", new { repoId = "orders-api", description = "b" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        release.SetResult(new GeneratedText(Draft, null));
        Assert.Equal(HttpStatusCode.OK, (await first).StatusCode);

        _gen.Requests.Clear();
        using var f3 = new Factory(new FakeGenerator(_ => throw new GeneratorException("claude zakończył się błędem", 502)));
        var c3 = await f3.AuthedClientAsync();
        await c3.PutAsJsonAsync("/api/settings", new { scanPath = _root });
        var id = (await (await c3.PostAsync("/api/scans", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        using (var resp = await c3.GetAsync($"/api/scans/{id}/events", HttpCompletionOption.ResponseHeadersRead))
        using (var reader = new StreamReader(await resp.Content.ReadAsStreamAsync()))
            while (await reader.ReadLineAsync() is not null) { }
        var failed = await c3.PostAsJsonAsync("/api/agents/generate", new { repoId = "orders-api", description = "a" });
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Contains("błędem", await failed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_writes_a_new_agent_file_inside_the_repo()
    {
        await StartAsync();
        var r = await _c.PostAsJsonAsync("/api/agents", new { repoId = "orders-api", content = Draft });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(".claude/agents/migration-reviewer.md", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("path").GetString());
        Assert.Equal(Draft, File.ReadAllText(AgentFile("orders-api", "migration-reviewer")));
        Assert.False(File.Exists(Path.Combine(_root, "orders-api", ".claude", "agents", "migration-reviewer.md.tmp")));
    }

    [Fact]
    public async Task Create_makes_the_agents_folder_when_it_is_missing()
    {
        await StartAsync();
        var r = await _c.PostAsJsonAsync("/api/agents", new { repoId = "plain", content = Draft });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.True(File.Exists(AgentFile("plain", "migration-reviewer")));
    }

    [Fact]
    public async Task Create_never_overwrites_an_existing_file()
    {
        await StartAsync();
        var existing = AgentFile("orders-api", "code-reviewer");
        var before = File.ReadAllText(existing);
        var r = await _c.PostAsJsonAsync("/api/agents", new { repoId = "orders-api", content = Draft.Replace("migration-reviewer", "code-reviewer") });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(before, File.ReadAllText(existing));

        Assert.Equal(HttpStatusCode.Created, (await _c.PostAsJsonAsync("/api/agents", new { repoId = "orders-api", content = Draft })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _c.PostAsJsonAsync("/api/agents", new { repoId = "orders-api", content = Draft })).StatusCode);
    }

    [Theory]
    [InlineData("---\nname: ../../evil\ndescription: A long enough description.\n---\nBody with enough text here.\n")]
    [InlineData("---\nname: Bad Name\ndescription: A long enough description.\n---\nBody with enough text here.\n")]
    [InlineData("just text")]
    [InlineData("")]
    public async Task Create_refuses_invalid_content_and_traversal_names(string content)
    {
        await StartAsync();
        var r = await _c.PostAsJsonAsync("/api/agents", new { repoId = "orders-api", content });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.False(File.Exists(Path.Combine(_root, "evil.md")));
        Assert.False(File.Exists(Path.Combine(_root, "orders-api", "evil.md")));
    }

    [Fact]
    public async Task Create_refuses_unknown_repos_and_ignores_client_paths()
    {
        await StartAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await _c.PostAsJsonAsync("/api/agents", new { repoId = "../orders-api", content = Draft })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _c.PostAsJsonAsync("/api/agents", new { repoId = "nope", content = Draft })).StatusCode);
        var r = await _c.PostAsJsonAsync("/api/agents", new { repoId = "orders-api", content = Draft, path = "../../pwned.md", dir = "/tmp" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.False(File.Exists(Path.Combine(_root, "pwned.md")));
    }

    [Fact]
    public async Task Create_refuses_when_the_claude_folder_is_a_symlink_leaving_the_repo()
    {
        if (OperatingSystem.IsWindows()) return;
        await StartAsync();
        Directory.CreateDirectory(Path.Combine(_outside, "agents"));
        var link = Path.Combine(_root, "plain", ".claude");
        Directory.CreateSymbolicLink(link, _outside);

        var r = await _c.PostAsJsonAsync("/api/agents", new { repoId = "plain", content = Draft });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Empty(Directory.GetFiles(Path.Combine(_outside, "agents")));
    }

    [Fact]
    public async Task Both_endpoints_need_the_token()
    {
        await StartAsync();
        var anon = _f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/agents/generate", new { repoId = "orders-api", description = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/agents", new { repoId = "orders-api", content = Draft })).StatusCode);
        Assert.Empty(_gen.Requests);
        Assert.False(File.Exists(AgentFile("orders-api", "migration-reviewer")));
    }

    public void Dispose()
    {
        _f?.Dispose();
        foreach (var d in new[] { _root, _outside }) { try { Directory.Delete(d, true); } catch { } }
    }
}
