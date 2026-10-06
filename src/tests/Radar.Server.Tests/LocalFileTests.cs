using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Radar.Server.Tests;

public class LocalFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "radar-local-" + Guid.NewGuid().ToString("N"));
    private readonly ServerFactory _f = new();
    private HttpClient _c = null!;

    public void Dispose() { _f.Dispose(); try { Directory.Delete(_root, true); } catch { /* best effort */ } }

    private string Repo => Path.Combine(_root, "app");
    private string Local => Path.Combine(Repo, "CLAUDE.local.md");
    private string Claude => Path.Combine(Repo, "CLAUDE.md");
    private string Exclude => Path.Combine(Repo, ".git", "info", "exclude");

    private static string Git(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-C", dir, "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false" }.Concat(args)) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return o;
    }

    private void Write(string rel, string text)
    {
        var p = Path.Combine(Repo, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    private static string Wf(string name, string desc, string when) => $"---\nname: {name}\ndescription: {desc}\nwhen: {when}\n---\nSteps.\n";

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

    /// <summary>A real repo with CLAUDE.md, one committed (public) workflow and one private one.</summary>
    private async Task StartAsync(bool realGit = true, bool claudeMd = true, Action? extra = null)
    {
        Directory.CreateDirectory(Repo);
        if (realGit) Git(Repo, "init", "-q"); else Directory.CreateDirectory(Path.Combine(Repo, ".git"));
        if (claudeMd) Write("CLAUDE.md", "# App\n");
        Write(".claude/workflows/release.md", Wf("release", "How to cut a release", "before tagging"));
        Write(".claude/workflows/scratch.md", Wf("scratch", "My own notes", "when I experiment"));
        if (realGit)
        {
            Git(Repo, "add", ".");
            Git(Repo, "commit", "-q", "-m", "init");
            Git(Repo, "rm", "--cached", "-q", ".claude/workflows/scratch.md");
            File.AppendAllText(Exclude, "/.claude/workflows/scratch.md\n");
        }
        extra?.Invoke();
        await ScanAsync();
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> Run(bool includePublic = false, bool confirm = false)
    {
        var r = await _c.PostAsJsonAsync("/api/local-file", new { repoId = "app", includePublic, confirm });
        return (r.StatusCode, await r.Content.ReadFromJsonAsync<JsonElement>());
    }

    private static string[] Ids(JsonElement body) => body.GetProperty("parts").EnumerateArray().Select(p => p.GetProperty("id").GetString()!).ToArray();

    [Fact]
    public async Task It_plans_first_then_writes_the_private_index_and_keeps_the_file_out_of_git()
    {
        await StartAsync();
        var (_, plan) = await Run();
        Assert.Equal(["exclude", "workflows-local"], Ids(plan));
        Assert.False(File.Exists(Local));

        var (_, done) = await Run(confirm: true);
        Assert.True(done.GetProperty("applied").GetBoolean());
        var text = File.ReadAllText(Local);
        Assert.Contains("<!-- radar:workflows -->", text);
        Assert.Contains("`scratch`: My own notes When: when I experiment File: `.claude/workflows/scratch.md`", text);
        Assert.DoesNotContain("release", text);                        // public: not in the private index
        Assert.Contains("/CLAUDE.local.md", File.ReadAllText(Exclude));
        Assert.DoesNotContain("CLAUDE.local.md", Git(Repo, "status", "--porcelain", "-uall"));
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        await StartAsync();
        await Run(confirm: true);
        var before = File.ReadAllText(Local);
        var (_, again) = await Run(confirm: true);
        Assert.Empty(Ids(again));
        Assert.False(again.GetProperty("applied").GetBoolean());
        Assert.Equal(before, File.ReadAllText(Local));
    }

    [Fact]
    public async Task Existing_notes_are_kept_and_the_block_follows_the_workflows()
    {
        await StartAsync(extra: () => File.WriteAllText(Local, "# My notes\nkeep this\n"));
        await Run(confirm: true);
        Assert.StartsWith("# My notes\nkeep this\n", File.ReadAllText(Local));

        // the workflow is made public: it leaves the private index and the empty block disappears
        Git(Repo, "add", "-f", ".claude/workflows/scratch.md");
        File.WriteAllText(Exclude, File.ReadAllText(Exclude).Replace("/.claude/workflows/scratch.md\n", ""));
        await ScanAsync();
        var (_, plan) = await Run();
        Assert.Contains(plan.GetProperty("parts").EnumerateArray(), p => p.GetProperty("id").GetString() == "workflows-local" && p.GetProperty("action").GetString() == "remove");
        await Run(confirm: true);
        var text = File.ReadAllText(Local);
        Assert.DoesNotContain("radar:workflows", text);
        Assert.Contains("keep this", text);
    }

    [Fact]
    public async Task Nothing_to_say_means_no_file_is_created()
    {
        await StartAsync(extra: () => File.Delete(Path.Combine(Repo, ".claude", "workflows", "scratch.md")));
        var (_, body) = await Run(confirm: true);
        Assert.Empty(Ids(body));
        Assert.False(File.Exists(Local));
    }

    [Fact]
    public async Task A_repo_with_only_AGENTS_md_keeps_reading_it()
    {
        await StartAsync(claudeMd: false, extra: () => Write("AGENTS.md", "# Agents\n"));
        var (_, body) = await Run(confirm: true);
        Assert.Contains("agents-md", Ids(body));
        Assert.Contains("<!-- radar:agents-md -->\n@AGENTS.md\n<!-- /radar:agents-md -->", File.ReadAllText(Local));
    }

    [Fact]
    public async Task With_a_CLAUDE_md_the_import_is_not_needed_but_a_missing_one_is_pointed_out()
    {
        await StartAsync(extra: () => Write("AGENTS.md", "# Agents\n"));
        var (_, body) = await Run(confirm: true);
        Assert.DoesNotContain("agents-md", Ids(body));
        Assert.Contains("agents-md-not-imported", body.GetProperty("notes").EnumerateArray().Select(n => n.GetString()));
        Assert.DoesNotContain("@AGENTS.md", File.ReadAllText(Local));
    }

    [Fact]
    public async Task Public_workflows_go_into_CLAUDE_md_only_when_asked()
    {
        await StartAsync();
        var (_, off) = await Run(confirm: true);
        Assert.DoesNotContain("workflows-public", Ids(off));
        Assert.Equal("# App\n", File.ReadAllText(Claude));

        var (_, plan) = await Run(includePublic: true);
        Assert.Equal(1, plan.GetProperty("publicCandidates").GetInt32());
        var (_, on) = await Run(includePublic: true, confirm: true);
        Assert.Contains("workflows-public", Ids(on));
        var text = File.ReadAllText(Claude);
        Assert.StartsWith("# App\n", text);
        Assert.Contains("`release`: How to cut a release When: before tagging File: `.claude/workflows/release.md`", text);
        Assert.DoesNotContain("scratch", text);
    }

    [Fact]
    public async Task Without_a_CLAUDE_md_the_public_block_is_skipped_with_a_note()
    {
        await StartAsync(claudeMd: false);
        var (_, body) = await Run(includePublic: true, confirm: true);
        Assert.DoesNotContain("workflows-public", Ids(body));
        Assert.Contains("no-claude-md", body.GetProperty("notes").EnumerateArray().Select(n => n.GetString()));
        Assert.False(File.Exists(Claude));
    }

    [Fact]
    public async Task Damaged_markers_and_unsafe_git_states_block_the_write()
    {
        await StartAsync(extra: () => File.WriteAllText(Local, "<!-- radar:workflows -->\nhalf a block\n"));
        var before = File.ReadAllText(Local);
        var (_, damaged) = await Run(confirm: true);
        Assert.Equal("local-damaged", damaged.GetProperty("blocked").GetString());
        Assert.Equal(before, File.ReadAllText(Local));
    }

    [Fact]
    public async Task A_tracked_CLAUDE_local_md_is_never_written()
    {
        await StartAsync(extra: () => { File.WriteAllText(Local, "# committed by mistake\n"); Git(Repo, "add", "-f", "CLAUDE.local.md"); Git(Repo, "commit", "-q", "-m", "x"); });
        var (_, body) = await Run(confirm: true);
        Assert.Equal("local-tracked", body.GetProperty("blocked").GetString());
        Assert.Equal("# committed by mistake\n", File.ReadAllText(Local));
    }

    [Fact]
    public async Task Without_a_real_git_repo_nothing_is_written()
    {
        await StartAsync(realGit: false, claudeMd: false, extra: () => Write("AGENTS.md", "# Agents\n"));   // something to write, but git cannot hide the file
        var (_, body) = await Run(confirm: true);
        Assert.Equal("no-git", body.GetProperty("blocked").GetString());
        Assert.False(File.Exists(Local));
    }

    [Fact]
    public async Task An_already_ignored_file_needs_no_exclude_line()
    {
        await StartAsync(extra: () => File.WriteAllText(Path.Combine(Repo, ".gitignore"), "CLAUDE.local.md\n"));
        var (_, body) = await Run(confirm: true);
        Assert.DoesNotContain("exclude", Ids(body));
        Assert.DoesNotContain("/CLAUDE.local.md", File.ReadAllText(Exclude));
        Assert.True(File.Exists(Local));
    }

    [Fact]
    public async Task Turning_on_the_second_brain_in_an_AGENTS_md_repo_keeps_AGENTS_md_readable_and_mentions_the_skill()
    {
        await StartAsync(claudeMd: false, extra: () => Write("AGENTS.md", "# Agents\n"));
        var vault = Path.Combine(Path.GetTempPath(), "radar-local-vault-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(HttpStatusCode.Created, (await _c.PostAsJsonAsync("/api/vault", new { path = vault })).StatusCode);
            var r = await _c.PostAsJsonAsync("/api/vault/projects", new { repoId = "app", confirm = true });
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            var text = File.ReadAllText(Local);
            Assert.Contains("<!-- radar:second-brain -->", text);
            Assert.Contains("radar-second-brain", text);
            Assert.Contains("@AGENTS.md", text);
        }
        finally { try { Directory.Delete(vault, true); } catch { /* best effort */ } }
    }

    [Fact]
    public async Task Unknown_repos_are_rejected()
    {
        await StartAsync();
        var r = await _c.PostAsJsonAsync("/api/local-file", new { repoId = "nope" });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}

public class LocalBlocksTests
{
    [Fact]
    public void Long_or_multi_line_descriptions_are_squeezed_into_one_short_line()
    {
        var text = Radar.Server.Features.LocalFile.LocalBlocks.Workflows([new("w", "first   line\nsecond " + new string('x', 400), "now", ".claude/workflows/w.md")], isPrivate: false);
        var line = text.Split('\n').Single(l => l.StartsWith("- `w`"));
        Assert.StartsWith("- `w`: first line second x", line);
        Assert.Contains("…", line);
        Assert.StartsWith("## Workflows\n", text);
    }
}
