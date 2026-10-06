using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Radar.Server.Tests;

public class VaultAccessTests : IDisposable
{
    // Claude Code adds **/.claude/settings.local.json to the user's global git excludes, which would make every file here "already ignored";
    // the tests need git to see no global configuration (the process-wide setting is harmless for the other tests).
    static VaultAccessTests()
    {
        var empty = Path.Combine(Path.GetTempPath(), "radar-no-git-config");
        Directory.CreateDirectory(empty);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", empty);
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", Path.Combine(empty, "none"));
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "radar-access-" + Guid.NewGuid().ToString("N"));
    private readonly string _vault = Path.Combine(Path.GetTempPath(), "radar-access-vault-" + Guid.NewGuid().ToString("N"));
    private readonly ServerFactory _f = new();
    private HttpClient _c = null!;

    public void Dispose()
    {
        _f.Dispose();
        foreach (var d in new[] { _root, _vault }) try { Directory.Delete(d, true); } catch { /* best effort */ }
    }

    private string Repo => Path.Combine(_root, "app");
    private string Settings => Path.Combine(Repo, ".claude", "settings.local.json");
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

    private async Task StartAsync(bool realGit = true, Action? extra = null)
    {
        Directory.CreateDirectory(Repo);
        if (realGit) Git(Repo, "init", "-q"); else Directory.CreateDirectory(Path.Combine(Repo, ".git"));
        File.WriteAllText(Path.Combine(Repo, "CLAUDE.md"), "# App\n");
        if (realGit) { Git(Repo, "add", "."); Git(Repo, "commit", "-q", "-m", "init"); }
        extra?.Invoke();

        _c = await _f.AuthedClientAsync();
        await _c.PutAsJsonAsync("/api/settings", new { scanPath = _root });
        var id = (await (await _c.PostAsync("/api/scans", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        using var resp = await _c.GetAsync($"/api/scans/{id}/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (await reader.ReadLineAsync(cts.Token) is not null) { }
        Assert.Equal(HttpStatusCode.Created, (await _c.PostAsJsonAsync("/api/vault", new { path = _vault })).StatusCode);
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> Enable(bool allowAccess, bool confirm)
    {
        var r = await _c.PostAsJsonAsync("/api/vault/projects", new { repoId = "app", confirm, allowAccess });
        return (r.StatusCode, await r.Content.ReadFromJsonAsync<JsonElement>());
    }

    private string[] Dirs()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Settings));
        return doc.RootElement.GetProperty("permissions").GetProperty("additionalDirectories").EnumerateArray().Select(e => e.GetString()!).ToArray();
    }

    [Fact]
    public async Task The_plan_shows_the_access_step_and_writes_nothing()
    {
        await StartAsync();
        var (status, body) = await Enable(allowAccess: true, confirm: false);
        Assert.Equal(HttpStatusCode.OK, status);
        var access = body.GetProperty("plan").GetProperty("access");
        Assert.Equal(".claude/settings.local.json", access.GetProperty("path").GetString());
        Assert.Equal("create", access.GetProperty("action").GetString());
        Assert.Equal("/.claude/settings.local.json", access.GetProperty("excludeEntry").GetString());
        Assert.False(File.Exists(Settings));
    }

    [Fact]
    public async Task Confirming_lets_Claude_read_the_vault_and_keeps_the_settings_file_out_of_git()
    {
        await StartAsync();
        var (status, _) = await Enable(allowAccess: true, confirm: true);
        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal([_vault], Dirs());
        Assert.Contains("/.claude/settings.local.json", File.ReadAllText(Exclude));
        Assert.DoesNotContain("settings.local.json", Git(Repo, "status", "--porcelain", "-uall"));
    }

    [Fact]
    public async Task Existing_settings_are_kept_and_a_second_run_adds_nothing()
    {
        await StartAsync(extra: () =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Settings)!);
            File.WriteAllText(Settings, "{\n  \"model\": \"opus\",\n  \"permissions\": { \"allow\": [\"Bash(npm test)\"], \"additionalDirectories\": [\"/other\"] }\n}\n");
        });
        await Enable(allowAccess: true, confirm: true);
        Assert.Equal(["/other", _vault], Dirs());
        using (var doc = JsonDocument.Parse(File.ReadAllText(Settings)))
        {
            Assert.Equal("opus", doc.RootElement.GetProperty("model").GetString());
            Assert.Equal("Bash(npm test)", doc.RootElement.GetProperty("permissions").GetProperty("allow")[0].GetString());
        }

        var before = File.ReadAllText(Settings);
        var (_, again) = await Enable(allowAccess: true, confirm: true);
        Assert.Equal("unchanged", again.GetProperty("plan").GetProperty("access").GetProperty("action").GetString());
        Assert.Equal(before, File.ReadAllText(Settings));
    }

    [Fact]
    public async Task A_file_git_already_ignores_needs_no_exclude_line()
    {
        await StartAsync(extra: () => File.WriteAllText(Path.Combine(Repo, ".gitignore"), ".claude/settings.local.json\n"));
        var (_, plan) = await Enable(allowAccess: true, confirm: false);
        Assert.False(plan.GetProperty("plan").GetProperty("access").TryGetProperty("excludeEntry", out _));
        await Enable(allowAccess: true, confirm: true);
        Assert.Equal([_vault], Dirs());
        Assert.DoesNotContain("radar:private", File.ReadAllText(Exclude));
    }

    [Fact]
    public async Task Without_asking_for_it_no_settings_file_is_touched()
    {
        await StartAsync();
        var (_, body) = await Enable(allowAccess: false, confirm: true);
        Assert.False(body.GetProperty("plan").TryGetProperty("access", out _));
        Assert.False(File.Exists(Settings));
        Assert.True(File.Exists(Path.Combine(Repo, "CLAUDE.local.md")));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("{ \"permissions\": 5 }")]
    [InlineData("{ \"permissions\": { \"additionalDirectories\": \"/x\" } }")]
    public async Task A_settings_file_that_is_not_plain_usable_JSON_blocks_everything(string content)
    {
        await StartAsync(extra: () => { Directory.CreateDirectory(Path.GetDirectoryName(Settings)!); File.WriteAllText(Settings, content); });
        var (status, body) = await Enable(allowAccess: true, confirm: true);
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("settings-invalid", body.GetProperty("code").GetString());
        Assert.Equal(content, File.ReadAllText(Settings));
        Assert.False(File.Exists(Path.Combine(Repo, "CLAUDE.local.md")));      // nothing at all was written
    }

    [Fact]
    public async Task A_tracked_settings_file_is_never_written()
    {
        await StartAsync(extra: () =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Settings)!);
            File.WriteAllText(Settings, "{}\n");
            Git(Repo, "add", "-f", ".claude/settings.local.json");
            Git(Repo, "commit", "-q", "-m", "oops");
        });
        var (status, body) = await Enable(allowAccess: true, confirm: true);
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("tracked", body.GetProperty("code").GetString());
        Assert.Equal("{}\n", File.ReadAllText(Settings));
    }

    [Fact]
    public async Task Without_a_real_git_repo_the_access_is_refused()
    {
        await StartAsync(realGit: false);
        var (status, body) = await Enable(allowAccess: true, confirm: true);
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("no-git", body.GetProperty("code").GetString());
        Assert.False(File.Exists(Settings));
    }
}
