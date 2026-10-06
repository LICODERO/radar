using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Radar.Server.Tests;

public class VisibilityTests : IDisposable
{
    private const string Agent = "---\nname: {0}\ndescription: Reviews pull requests for correctness and style\ntools: Read\n---\n\nYou review code.\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "radar-vis-" + Guid.NewGuid().ToString("N"));
    private readonly ServerFactory _f = new();
    private HttpClient _c = null!;

    public void Dispose() { _f.Dispose(); try { Directory.Delete(_root, true); } catch { /* best effort */ } }

    private string Repo => Path.Combine(_root, "app");
    private string Exclude => Path.Combine(Repo, ".git", "info", "exclude");

    private static string Git(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-C", dir, "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false" }.Concat(args)) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)} failed");
        return o;
    }

    private void Write(string rel, string text)
    {
        var p = Path.Combine(Repo, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    /// <summary>A real repo with a committed agent (shared), an untracked one (new), a committed skill and a committed workflow.</summary>
    private async Task StartAsync(bool realGit = true)
    {
        Directory.CreateDirectory(Repo);
        if (realGit) Git(Repo, "init", "-q"); else Directory.CreateDirectory(Path.Combine(Repo, ".git"));
        Write(".claude/agents/shared.md", string.Format(Agent, "shared"));
        Write(".claude/agents/fresh.md", string.Format(Agent, "fresh"));
        Write(".claude/skills/deploy/SKILL.md", "---\nname: deploy\ndescription: Deploys to staging\n---\nSteps.\n");
        Write(".claude/workflows/ship.md", "---\nname: ship\ndescription: Ship it\n---\nGo.\n");
        if (realGit)
        {
            Git(Repo, "add", ".claude/agents/shared.md", ".claude/skills", ".claude/workflows");
            Git(Repo, "commit", "-q", "-m", "init");
        }
        _c = await _f.AuthedClientAsync();
        await _c.PutAsJsonAsync("/api/settings", new { scanPath = _root });
        var id = (await (await _c.PostAsync("/api/scans", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        using var resp = await _c.GetAsync($"/api/scans/{id}/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (await reader.ReadLineAsync(cts.Token) is not null) { }
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> Set(string path, string visibility, bool confirm = false)
    {
        var r = await _c.PostAsJsonAsync("/api/visibility", new { repoId = "app", path, visibility, confirm });
        return (r.StatusCode, await r.Content.ReadFromJsonAsync<JsonElement>());
    }

    private static string[] Kinds(JsonElement body) => body.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("kind").GetString()!).ToArray();

    [Fact]
    public async Task Without_confirm_it_only_plans_and_writes_nothing()
    {
        await StartAsync();
        var (status, body) = await Set(".claude/agents/fresh.md", "private");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("applied").GetBoolean());
        Assert.Equal("untracked", body.GetProperty("current").GetString());
        Assert.Equal(["exclude-add"], Kinds(body));
        Assert.DoesNotContain("radar:private", File.ReadAllText(Exclude));
    }

    [Fact]
    public async Task An_untracked_agent_becomes_private_through_the_exclude_block()
    {
        await StartAsync();
        File.AppendAllText(Exclude, "*.log\n");
        var (_, body) = await Set(".claude/agents/fresh.md", "private", confirm: true);
        Assert.True(body.GetProperty("applied").GetBoolean());
        Assert.Equal("private", body.GetProperty("visibility").GetString());
        var text = File.ReadAllText(Exclude);
        Assert.Contains("*.log", text);
        Assert.Contains("/.claude/agents/fresh.md", text);
        Assert.Equal("", Git(Repo, "status", "--porcelain", "--", ".claude/agents/fresh.md").Trim());
    }

    [Fact]
    public async Task A_tracked_agent_is_taken_out_of_the_index_but_stays_on_disk()
    {
        await StartAsync();
        var (_, plan) = await Set(".claude/agents/shared.md", "private");
        Assert.Equal(["exclude-add", "git-rm-cached"], Kinds(plan));
        Assert.Contains("commit-removal", plan.GetProperty("notes").EnumerateArray().Select(n => n.GetString()));

        var (_, done) = await Set(".claude/agents/shared.md", "private", confirm: true);
        Assert.Equal("private", done.GetProperty("visibility").GetString());
        Assert.True(File.Exists(Path.Combine(Repo, ".claude", "agents", "shared.md")));
        Assert.DoesNotContain(".claude/agents/shared.md", Git(Repo, "ls-files"));
        Assert.Contains("D  .claude/agents/shared.md", Git(Repo, "status", "--porcelain"));
    }

    [Fact]
    public async Task A_skill_folder_and_a_workflow_work_too()
    {
        await StartAsync();
        var (_, skill) = await Set(".claude/skills/deploy/SKILL.md", "private", confirm: true);
        Assert.Equal("private", skill.GetProperty("visibility").GetString());
        Assert.Contains("/.claude/skills/deploy/", File.ReadAllText(Exclude));
        Assert.DoesNotContain(".claude/skills/deploy", Git(Repo, "ls-files"));

        var (_, wf) = await Set(".claude/workflows/ship.md", "private", confirm: true);
        Assert.Equal("private", wf.GetProperty("visibility").GetString());
    }

    [Fact]
    public async Task Making_it_public_again_removes_only_its_own_line()
    {
        await StartAsync();
        File.AppendAllText(Exclude, "*.log\n");
        await Set(".claude/agents/fresh.md", "private", confirm: true);
        await Set(".claude/skills/deploy/SKILL.md", "private", confirm: true);

        var (_, body) = await Set(".claude/agents/fresh.md", "public", confirm: true);
        Assert.Equal("untracked", body.GetProperty("visibility").GetString());
        Assert.Contains("commit-to-share", body.GetProperty("notes").EnumerateArray().Select(n => n.GetString()));
        var text = File.ReadAllText(Exclude);
        Assert.DoesNotContain("/.claude/agents/fresh.md", text);
        Assert.Contains("/.claude/skills/deploy/", text);
        Assert.Contains("*.log", text);

        await Set(".claude/skills/deploy/SKILL.md", "public", confirm: true);
        Assert.DoesNotContain("radar:private", File.ReadAllText(Exclude));
    }

    [Fact]
    public async Task An_item_hidden_by_someone_elses_rule_is_not_touched()
    {
        await StartAsync();
        File.AppendAllText(Exclude, "/.claude/agents/fresh.md\n"); // the user's own line, outside RADAR's block
        var before = File.ReadAllText(Exclude);
        var (_, body) = await Set(".claude/agents/fresh.md", "public", confirm: true);
        Assert.Equal("other-rule", body.GetProperty("blocked").GetString());
        Assert.False(body.GetProperty("applied").GetBoolean());
        Assert.Equal(before, File.ReadAllText(Exclude));
    }

    [Fact]
    public async Task A_gitignore_that_still_hides_it_rolls_the_change_back()
    {
        await StartAsync();
        await Set(".claude/agents/fresh.md", "private", confirm: true);
        File.WriteAllText(Path.Combine(Repo, ".gitignore"), ".claude/agents/fresh.md\n");
        var before = File.ReadAllText(Exclude);
        var (_, body) = await Set(".claude/agents/fresh.md", "public", confirm: true);
        Assert.Equal("other-rule", body.GetProperty("blocked").GetString());
        Assert.Equal(before, File.ReadAllText(Exclude));
    }

    [Fact]
    public async Task Damaged_markers_are_never_rewritten()
    {
        await StartAsync();
        File.AppendAllText(Exclude, "# >>> radar:private >>>\n/x\n");
        var before = File.ReadAllText(Exclude);
        var (_, body) = await Set(".claude/agents/fresh.md", "private", confirm: true);
        Assert.Equal("exclude-damaged", body.GetProperty("blocked").GetString());
        Assert.Equal(before, File.ReadAllText(Exclude));
    }

    [Fact]
    public async Task Without_a_real_git_repo_nothing_is_written()
    {
        await StartAsync(realGit: false);
        var (_, body) = await Set(".claude/agents/fresh.md", "private", confirm: true);
        Assert.Equal("no-git", body.GetProperty("blocked").GetString());
    }

    [Fact]
    public async Task Only_scanned_items_and_known_values_are_accepted()
    {
        await StartAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Set(".claude/agents/ghost.md", "private")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Set("CLAUDE.md", "private")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Set("../outside.md", "private")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Set(".claude/agents/fresh.md", "secret")).Status);
        var unknownRepo = await _c.PostAsJsonAsync("/api/visibility", new { repoId = "nope", path = ".claude/agents/fresh.md", visibility = "private" });
        Assert.Equal(HttpStatusCode.NotFound, unknownRepo.StatusCode);
    }

    [Fact]
    public async Task A_private_flag_is_idempotent()
    {
        await StartAsync();
        await Set(".claude/agents/fresh.md", "private", confirm: true);
        var once = File.ReadAllText(Exclude);
        var (_, again) = await Set(".claude/agents/fresh.md", "private", confirm: true);
        Assert.Empty(Kinds(again));
        Assert.Equal(once, File.ReadAllText(Exclude));
    }
}
