using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Radar.Server.Tests;

public class SkillCopyTests : IDisposable
{
    private const string Skill = "---\nname: deploy\ndescription: Deploys the app to staging\n---\n\nRun scripts/run.sh then read references/steps.md.\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "radar-skill-" + Guid.NewGuid().ToString("N"));
    private readonly ServerFactory _f = new();
    private HttpClient _c = null!;

    public void Dispose() { _f.Dispose(); try { Directory.Delete(_root, true); } catch { /* best effort */ } }

    private string Dir(string repo) => Path.Combine(_root, repo);
    private string SkillDir(string repo) => Path.Combine(Dir(repo), ".claude", "skills", "deploy");

    private static string Git(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-C", dir }.Concat(args)) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return o;
    }

    private void Put(string repo, string rel, string text)
    {
        var p = Path.Combine(SkillDir(repo), rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    /// <summary>src has the skill with nested files, a binary asset and a symlink; dst-a / dst-b are real repos, dst-fake has an empty .git folder, dst-has already has it.</summary>
    private async Task StartAsync(Action? extra = null)
    {
        foreach (var r in new[] { "src", "dst-a", "dst-b", "dst-has" }) { Directory.CreateDirectory(Dir(r)); Git(Dir(r), "init", "-q"); }
        Directory.CreateDirectory(Path.Combine(Dir("dst-fake"), ".git"));
        Put("src", "SKILL.md", Skill);
        Put("src", "references/steps.md", "1. build\n2. ship\n");
        Put("src", "scripts/run.sh", "#!/bin/sh\necho deploy\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(SkillDir("src"), "scripts", "run.sh"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllBytes(Path.Combine(SkillDir("src"), "logo.png"), [0x89, 0x50, 0x00, 0x01, 0x02]);
        if (!OperatingSystem.IsWindows()) File.CreateSymbolicLink(Path.Combine(SkillDir("src"), "outside"), "/etc");
        Put("dst-has", "SKILL.md", "---\nname: deploy\ndescription: Their own deploy skill\n---\nOwn.\n");
        extra?.Invoke();

        _c = await _f.AuthedClientAsync();
        await _c.PutAsJsonAsync("/api/settings", new { scanPath = _root });
        var id = (await (await _c.PostAsync("/api/scans", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        using var resp = await _c.GetAsync($"/api/scans/{id}/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (await reader.ReadLineAsync(cts.Token) is not null) { }
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> Copy(object body)
    {
        var r = await _c.PostAsJsonAsync("/api/skills/copy", body);
        return (r.StatusCode, await r.Content.ReadFromJsonAsync<JsonElement>());
    }

    private static string St(JsonElement body, string repo) =>
        body.GetProperty("targets").EnumerateArray().Single(t => t.GetProperty("repoId").GetString() == repo).GetProperty("status").GetString()!;

    [Fact]
    public async Task The_plan_lists_the_files_and_what_is_skipped_and_writes_nothing()
    {
        await StartAsync();
        var (status, body) = await Copy(new { fromRepoId = "src", name = "deploy", toRepoIds = new[] { "dst-a", "dst-has" } });
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("written").GetBoolean());
        Assert.Equal(["SKILL.md", "references/steps.md", "scripts/run.sh"], body.GetProperty("files").EnumerateArray().Select(f => f.GetString()!).Order(StringComparer.Ordinal).ToArray());
        var skipped = body.GetProperty("skipped").EnumerateArray().Select(s => s.GetProperty("reason").GetString()).Order().ToArray();
        Assert.Contains("binary", skipped);
        if (!OperatingSystem.IsWindows()) Assert.Contains("symlink", skipped);
        Assert.Equal("ready", St(body, "dst-a"));
        Assert.Equal("exists", St(body, "dst-has"));
        Assert.False(Directory.Exists(SkillDir("dst-a")));
    }

    [Fact]
    public async Task Confirming_copies_the_whole_folder_keeps_the_executable_bit_and_never_overwrites()
    {
        await StartAsync();
        var theirs = File.ReadAllText(Path.Combine(SkillDir("dst-has"), "SKILL.md"));
        var (_, body) = await Copy(new { fromRepoId = "src", name = "deploy", toRepoIds = new[] { "dst-a", "dst-has" }, confirm = true });
        Assert.Equal("created", St(body, "dst-a"));
        Assert.Equal(Skill, File.ReadAllText(Path.Combine(SkillDir("dst-a"), "SKILL.md")));
        Assert.Equal("1. build\n2. ship\n", File.ReadAllText(Path.Combine(SkillDir("dst-a"), "references", "steps.md")));
        Assert.False(File.Exists(Path.Combine(SkillDir("dst-a"), "logo.png")));
        Assert.False(File.Exists(Path.Combine(SkillDir("dst-a"), "outside")));
        if (!OperatingSystem.IsWindows())
            Assert.NotEqual(UnixFileMode.None, File.GetUnixFileMode(Path.Combine(SkillDir("dst-a"), "scripts", "run.sh")) & UnixFileMode.UserExecute);
        Assert.Equal(theirs, File.ReadAllText(Path.Combine(SkillDir("dst-has"), "SKILL.md")));
    }

    [Fact]
    public async Task A_private_copy_is_hidden_from_git_and_skipped_where_that_is_impossible()
    {
        await StartAsync();
        var (_, body) = await Copy(new { fromRepoId = "src", name = "deploy", toRepoIds = new[] { "dst-a", "dst-fake" }, visibility = "private", confirm = true });
        Assert.Equal("created", St(body, "dst-a"));
        Assert.Equal("cannot-hide", St(body, "dst-fake"));
        Assert.False(Directory.Exists(SkillDir("dst-fake")));
        Assert.Contains("/.claude/skills/deploy/", File.ReadAllText(Path.Combine(Dir("dst-a"), ".git", "info", "exclude")));
        Assert.Equal("", Git(Dir("dst-a"), "status", "--porcelain", "-uall").Trim());
    }

    [Fact]
    public async Task Without_a_visibility_the_copy_shows_up_in_git_changes()
    {
        await StartAsync();
        await Copy(new { fromRepoId = "src", name = "deploy", toRepoIds = new[] { "dst-b" }, confirm = true });
        Assert.Contains(".claude/skills/deploy/SKILL.md", Git(Dir("dst-b"), "status", "--porcelain", "-uall"));
    }

    [Fact]
    public async Task Too_many_files_or_too_much_data_refuse_the_copy()
    {
        await StartAsync(() => { for (var i = 0; i < 55; i++) Put("src", $"references/r{i}.md", "x"); });
        var (status, _) = await Copy(new { fromRepoId = "src", name = "deploy", toRepoIds = new[] { "dst-a" } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.False(Directory.Exists(SkillDir("dst-a")));
    }

    [Fact]
    public async Task Too_much_data_is_refused()
    {
        await StartAsync(() => Put("src", "references/big.md", new string('x', 1100 * 1024)));
        var (status, _) = await Copy(new { fromRepoId = "src", name = "deploy", toRepoIds = new[] { "dst-a" } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
    }

    [Fact]
    public async Task Unknown_skills_repos_and_empty_targets_are_rejected()
    {
        await StartAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Copy(new { fromRepoId = "src", name = "ghost", toRepoIds = new[] { "dst-a" } })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Copy(new { fromRepoId = "nope", name = "deploy", toRepoIds = new[] { "dst-a" } })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Copy(new { fromRepoId = "src", name = "deploy", toRepoIds = Array.Empty<string>() })).Status);
        var (_, unknownTarget) = await Copy(new { fromRepoId = "src", name = "deploy", toRepoIds = new[] { "ghost-repo" } });
        Assert.Equal("unknown-repo", St(unknownTarget, "ghost-repo"));
    }
}
