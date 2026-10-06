using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Radar.Server.Features.Agents;
using Radar.Server.Features.Items;

namespace Radar.Server.Tests;

public class ItemPromptTests
{
    [Fact]
    public void A_skill_prompt_carries_only_the_agreed_data()
    {
        var p = ItemPrompt.Build(new AgentGenerationRequest("deploy na staging", ".NET", ["agent-a"], "skill", ["deploy-old"], ["wf"]));
        Assert.Contains("deploy na staging", p);
        Assert.Contains(".NET", p);
        Assert.Contains("deploy-old", p);
        Assert.DoesNotContain("agent-a", p);
        Assert.Contains("SKILL.md", p);
    }

    [Fact]
    public void A_workflow_prompt_offers_the_repos_agents_and_skills_to_refer_to()
    {
        var p = ItemPrompt.Build(new AgentGenerationRequest("jak dodać endpoint", "Angular", ["api-builder"], "workflow", ["ef-migrations"], ["commit"]));
        Assert.Contains("api-builder", p);
        Assert.Contains("ef-migrations", p);
        Assert.Contains("commit", p);
        Assert.Contains("ONLY from the lists above", p);
    }

    [Fact]
    public void Agents_keep_their_original_prompt()
    {
        var r = new AgentGenerationRequest("opis", ".NET", ["x"]);
        Assert.Equal(AgentPrompt.Build("opis", ".NET", ["x"]), ItemPrompt.Build(r));
        Assert.Equal(AgentPrompt.SystemPrompt, ItemPrompt.SystemFor("agent"));
    }
}

public class ItemEndpointTests : IDisposable
{
    private const string SkillDraft = "---\nname: staging-deploy\ndescription: Deploys the app to staging; use before a release.\n---\n\n1. Run the build.\n2. Run the deploy script.\n";
    private const string WorkflowDraft = "---\nname: add-endpoint\ndescription: How to add an API endpoint.\nwhen: when adding an endpoint\nagents: api-builder\n---\n\n1. Write the test.\n2. Implement it.\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "radar-items-" + Guid.NewGuid().ToString("N"));
    private FakeGenerator _gen = new(_ => Task.FromResult(new GeneratedText(SkillDraft, 0.02m)));
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

    private static string Git(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-C", dir }.Concat(args)) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return o;
    }

    private string Repo(string r) => Path.Combine(_root, r);

    private async Task StartAsync()
    {
        Directory.CreateDirectory(Repo("real")); Git(Repo("real"), "init", "-q");
        Directory.CreateDirectory(Path.Combine(Repo("fake"), ".git"));
        Directory.CreateDirectory(Path.Combine(Repo("real"), ".claude", "agents"));
        File.WriteAllText(Path.Combine(Repo("real"), ".claude", "agents", "api-builder.md"), "---\nname: api-builder\ndescription: Builds API endpoints\n---\nBody of the agent.");
        Directory.CreateDirectory(Path.Combine(Repo("real"), ".claude", "skills", "taken"));
        File.WriteAllText(Path.Combine(Repo("real"), ".claude", "skills", "taken", "SKILL.md"), "---\nname: taken\ndescription: Already here in the repo\n---\nBody of the skill.");

        _f = new Factory(_gen);
        _c = await _f.AuthedClientAsync();
        await _c.PutAsJsonAsync("/api/settings", new { scanPath = _root });
        var id = (await (await _c.PostAsync("/api/scans", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        using var resp = await _c.GetAsync($"/api/scans/{id}/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (await reader.ReadLineAsync(cts.Token) is not null) { }
    }

    public void Dispose() { _f?.Dispose(); try { Directory.Delete(_root, true); } catch { /* best effort */ } }

    [Fact]
    public async Task Generate_returns_a_skill_draft_and_sends_only_names_and_the_description()
    {
        await StartAsync();
        var r = await _c.PostAsJsonAsync("/api/items/generate", new { kind = "skill", repoId = "real", description = "deploy na staging" });
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("valid").GetBoolean());
        Assert.Equal(".claude/skills/staging-deploy/SKILL.md", body.GetProperty("path").GetString());
        Assert.False(Directory.Exists(Path.Combine(Repo("real"), ".claude", "skills", "staging-deploy")));
        var req = Assert.Single(_gen.Requests);
        Assert.Equal("skill", req.Kind);
        Assert.Equal(["taken"], req.ExistingSkills);
        Assert.Equal(["api-builder"], req.ExistingAgents);
    }

    [Fact]
    public async Task Generate_flags_a_name_that_is_taken_and_rejects_bad_input()
    {
        _gen = new FakeGenerator(_ => Task.FromResult(new GeneratedText(SkillDraft.Replace("staging-deploy", "taken"), null)));
        await StartAsync();
        var taken = await (await _c.PostAsJsonAsync("/api/items/generate", new { kind = "skill", repoId = "real", description = "x" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(taken.GetProperty("valid").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await _c.PostAsJsonAsync("/api/items/generate", new { kind = "agent", repoId = "real", description = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _c.PostAsJsonAsync("/api/items/generate", new { kind = "skill", repoId = "real", description = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _c.PostAsJsonAsync("/api/items/generate", new { kind = "skill", repoId = "nope", description = "x" })).StatusCode);
    }

    [Fact]
    public async Task A_private_skill_gets_its_own_folder_and_is_hidden_from_git()
    {
        await StartAsync();
        var r = await _c.PostAsJsonAsync("/api/items", new { kind = "skill", repoId = "real", content = SkillDraft, visibility = "private" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var file = Path.Combine(Repo("real"), ".claude", "skills", "staging-deploy", "SKILL.md");
        Assert.Equal(SkillDraft, File.ReadAllText(file));
        Assert.Contains("/.claude/skills/staging-deploy/", File.ReadAllText(Path.Combine(Repo("real"), ".git", "info", "exclude")));
        Assert.DoesNotContain("staging-deploy", Git(Repo("real"), "status", "--porcelain", "-uall"));
    }

    [Fact]
    public async Task A_public_workflow_is_written_and_visible_to_git()
    {
        await StartAsync();
        var r = await _c.PostAsJsonAsync("/api/items", new { kind = "workflow", repoId = "real", content = WorkflowDraft, visibility = "public" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(".claude/workflows/add-endpoint.md", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("path").GetString());
        Assert.Equal(WorkflowDraft, File.ReadAllText(Path.Combine(Repo("real"), ".claude", "workflows", "add-endpoint.md")));
        Assert.Contains(".claude/workflows/add-endpoint.md", Git(Repo("real"), "status", "--porcelain", "-uall"));
    }

    [Fact]
    public async Task A_private_item_is_not_created_when_it_cannot_be_hidden_and_nothing_is_overwritten()
    {
        await StartAsync();
        var r = await _c.PostAsJsonAsync("/api/items", new { kind = "workflow", repoId = "fake", content = WorkflowDraft, visibility = "private" });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.False(File.Exists(Path.Combine(Repo("fake"), ".claude", "workflows", "add-endpoint.md")));

        var dup = await _c.PostAsJsonAsync("/api/items", new { kind = "skill", repoId = "real", content = SkillDraft.Replace("staging-deploy", "taken") });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Contains("Body of the skill", File.ReadAllText(Path.Combine(Repo("real"), ".claude", "skills", "taken", "SKILL.md")));
    }

    [Fact]
    public async Task Create_validates_kind_and_content()
    {
        await StartAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await _c.PostAsJsonAsync("/api/items", new { kind = "agent", repoId = "real", content = SkillDraft })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _c.PostAsJsonAsync("/api/items", new { kind = "skill", repoId = "real", content = "no frontmatter" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _c.PostAsJsonAsync("/api/items", new { kind = "skill", repoId = "real", content = "" })).StatusCode);
    }
}
