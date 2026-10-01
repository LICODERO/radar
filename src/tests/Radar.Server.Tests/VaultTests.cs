using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Radar.Scanner;
using Radar.Server.Features.Vault;
using Radar.Server.Infrastructure.Storage;

namespace Radar.Server.Tests;

public class MarkedBlockTests
{
    private const string S = "<!-- a -->";
    private const string E = "<!-- /a -->";

    [Fact]
    public void Creates_the_block_in_empty_text()
    {
        Assert.Equal($"{S}\nhello\n{E}\n", MarkedBlock.Upsert(null, S, E, "hello"));
        Assert.Equal($"{S}\nhello\n{E}\n", MarkedBlock.Upsert("", S, E, "hello"));
    }

    [Fact]
    public void Appends_after_existing_text_and_keeps_it()
    {
        var result = MarkedBlock.Upsert("# Mine\n\nnotes\n", S, E, "hello");
        Assert.Equal($"# Mine\n\nnotes\n\n{S}\nhello\n{E}\n", result);
    }

    [Fact]
    public void Replaces_only_what_is_between_the_markers()
    {
        var text = $"before\n{S}\nold\n{E}\nafter\n";
        Assert.Equal($"before\n{S}\nnew\n{E}\nafter\n", MarkedBlock.Upsert(text, S, E, "new"));
    }

    [Fact]
    public void Is_idempotent()
    {
        var once = MarkedBlock.Upsert("# Mine\n", S, E, "hello")!;
        Assert.Equal(once, MarkedBlock.Upsert(once, S, E, "hello"));
    }

    [Theory]
    [InlineData("<!-- a -->\nno end\n")]
    [InlineData("no start\n<!-- /a -->\n")]
    [InlineData("<!-- /a -->\n<!-- a -->\n")]
    public void Refuses_damaged_markers(string text) => Assert.Null(MarkedBlock.Upsert(text, S, E, "x"));

    [Fact]
    public void Keeps_the_line_endings_of_the_file()
    {
        var result = MarkedBlock.Upsert("one\r\ntwo\r\n", S, E, "a\nb")!;
        Assert.DoesNotContain("\n", result.Replace("\r\n", ""));
    }
}

public class VaultTemplatesTests
{
    [Fact]
    public void Embedded_templates_are_present_and_filled()
    {
        Assert.Contains("How to use this vault", VaultTemplates.IndexMain());
        var index = VaultTemplates.ProjectIndex("apps/orders api", "orders-api");
        Assert.Contains("# orders-api", index);
        Assert.Contains("id=apps%2Forders%20api -->", index);
        Assert.DoesNotContain("{{", index);
        Assert.StartsWith("---\nname: radar-second-brain\n", VaultTemplates.Read("SKILL.md"));
    }
}

public sealed class VaultFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "radar-vault-tests-" + Guid.NewGuid().ToString("N"));
    public string DataDir => Path.Combine(Root, "data");
    public string Repos => Path.Combine(Root, "repos");
    public string VaultDir => Path.Combine(Root, "vault");
    public JsonFileStore Store { get; }
    public VaultService Service { get; }

    public VaultFixture()
    {
        Directory.CreateDirectory(Repos);
        Store = new JsonFileStore(DataDir);
        Service = new VaultService(Store);
    }

    public (ScanResult Scan, RepoInfo Repo) Repo(string id)
    {
        Directory.CreateDirectory(Path.Combine(Repos, id, ".git"));
        return (Scan(Repo_(id)), Repo_(id));
    }

    public RepoInfo Repo_(string id) => new(id, Path.GetFileName(id), id, "XX", "", [], new ClaudeMdInfo(false, "CLAUDE.md"), [], [], new CoverageInfo(0, new CoverageParts(0, 0, 0)), []);

    public ScanResult Scan(params RepoInfo[] repos) =>
        new(ScanResult.CurrentSchemaVersion, Repos, DateTimeOffset.Now, 0, new SummaryInfo(repos.Length, 0, 0, 0, 0, [], 0), repos, [], [], []);

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch { /* best effort */ }
    }
}

public class VaultServiceTests : IDisposable
{
    private readonly VaultFixture _t = new();
    public void Dispose() => _t.Dispose();

    [Fact]
    public void Starts_unconfigured()
    {
        var s = _t.Service.GetStatus();
        Assert.Equal("none", s.State);
        Assert.Null(s.Path);
    }

    [Fact]
    public void Creates_a_vault_with_marker_and_main_index_and_remembers_it()
    {
        var r = _t.Service.Create(_t.VaultDir);
        Assert.Equal(VaultOutcome.Ok, r.Outcome);
        Assert.True(File.Exists(Path.Combine(_t.VaultDir, VaultFiles.Marker)));
        var main = File.ReadAllText(Path.Combine(_t.VaultDir, VaultFiles.IndexMain));
        Assert.Contains("How to use this vault", main);
        Assert.Equal(_t.VaultDir, _t.Store.Load().VaultPath);
        Assert.Equal("ok", _t.Service.GetStatus().State);
    }

    [Fact]
    public void Creates_into_an_existing_empty_folder()
    {
        Directory.CreateDirectory(_t.VaultDir);
        Assert.Equal(VaultOutcome.Ok, _t.Service.Create(_t.VaultDir).Outcome);
    }

    [Fact]
    public void Refuses_a_folder_that_is_not_empty_and_changes_nothing()
    {
        Directory.CreateDirectory(_t.VaultDir);
        File.WriteAllText(Path.Combine(_t.VaultDir, "notes.md"), "mine");
        Assert.Equal(VaultOutcome.NotEmpty, _t.Service.Create(_t.VaultDir).Outcome);
        Assert.False(File.Exists(Path.Combine(_t.VaultDir, VaultFiles.Marker)));
        Assert.Null(_t.Store.Load().VaultPath);
    }

    [Fact]
    public void Refuses_to_create_over_an_existing_vault_and_never_overwrites_it()
    {
        _t.Service.Create(_t.VaultDir);
        var main = Path.Combine(_t.VaultDir, VaultFiles.IndexMain);
        File.WriteAllText(main, "edited");
        Assert.Equal(VaultOutcome.AlreadyVault, _t.Service.Create(_t.VaultDir).Outcome);
        Assert.Equal("edited", File.ReadAllText(main));
    }

    [Fact]
    public void Refuses_a_folder_inside_a_git_repository()
    {
        _t.Repo("api");
        var inside = Path.Combine(_t.Repos, "api", "docs", "vault");
        Assert.Equal(VaultOutcome.InsideRepo, _t.Service.Create(inside).Outcome);
        Assert.Equal(VaultOutcome.InsideRepo, _t.Service.Create(Path.Combine(_t.Repos, "api")).Outcome);
        Assert.False(Directory.Exists(inside));
    }

    [Fact]
    public void Refuses_a_drive_root_and_a_file()
    {
        Assert.Equal(VaultOutcome.PathIsRoot, _t.Service.Create(Path.GetPathRoot(_t.Root)!).Outcome);
        var file = Path.Combine(_t.Root, "a-file");
        File.WriteAllText(file, "x");
        Assert.Equal(VaultOutcome.PathInvalid, _t.Service.Create(file).Outcome);
    }

    [Fact]
    public void A_moved_vault_shows_as_missing_and_can_be_linked_again()
    {
        _t.Service.Create(_t.VaultDir);
        var moved = Path.Combine(_t.Root, "moved");
        Directory.Move(_t.VaultDir, moved);

        var missing = _t.Service.GetStatus();
        Assert.Equal("missing", missing.State);
        Assert.Equal("folder-missing", missing.Reason);

        Assert.Equal(VaultOutcome.Ok, _t.Service.Relink(moved).Outcome);
        Assert.Equal("ok", _t.Service.GetStatus().State);
        Assert.Equal(moved, _t.Store.Load().VaultPath);
    }

    [Fact]
    public void Relink_requires_the_marker_and_a_supported_version()
    {
        Directory.CreateDirectory(_t.VaultDir);
        Assert.Equal(VaultOutcome.NotAVault, _t.Service.Relink(_t.VaultDir).Outcome);
        Assert.Equal(VaultOutcome.FolderMissing, _t.Service.Relink(Path.Combine(_t.Root, "nope")).Outcome);

        File.WriteAllText(Path.Combine(_t.VaultDir, VaultFiles.Marker), "{\"app\":\"R.A.D.A.R.\",\"kind\":\"second-brain\",\"version\":99,\"createdAt\":\"2026-01-01T00:00:00Z\"}");
        Assert.Equal(VaultOutcome.UnsupportedVersion, _t.Service.Relink(_t.VaultDir).Outcome);

        File.WriteAllText(Path.Combine(_t.VaultDir, VaultFiles.Marker), "not json");
        Assert.Equal(VaultOutcome.NotAVault, _t.Service.Relink(_t.VaultDir).Outcome);
        Assert.Null(_t.Store.Load().VaultPath);
    }

    [Theory]
    [InlineData("orders-api", "orders-api")]
    [InlineData("My App", "My-App")]
    [InlineData("../evil", "evil")]
    [InlineData("...", "project")]
    [InlineData("a/b\\c", "a-b-c")]
    public void Folder_names_are_safe(string repoName, string expected) => Assert.Equal(expected, VaultService.FolderName(repoName));
}

public class VaultProjectTests : IDisposable
{
    private readonly VaultFixture _t = new();
    public void Dispose() => _t.Dispose();

    private string ClaudeLocal(string repo) => Path.Combine(_t.Repos, repo, "CLAUDE.local.md");

    [Fact]
    public void Needs_a_working_vault()
    {
        var (scan, repo) = _t.Repo("api");
        Assert.Equal(EnableOutcome.VaultNotReady, _t.Service.Enable(scan, repo, apply: false).Outcome);
    }

    [Fact]
    public void Preview_writes_nothing_and_lists_what_would_be_created()
    {
        _t.Service.Create(_t.VaultDir);
        var (scan, repo) = _t.Repo("api");

        var r = _t.Service.Enable(scan, repo, apply: false);

        Assert.Equal(EnableOutcome.Ok, r.Outcome);
        Assert.False(r.Applied);
        Assert.Equal(5, r.Plan!.Creates.Count); // project folder, raw, memory, outputs, _index.md
        Assert.False(r.Plan.AlreadyEnabled);
        Assert.Contains(_t.VaultDir, r.Plan.Block);
        Assert.False(Directory.Exists(Path.Combine(_t.VaultDir, "api")));
        Assert.False(File.Exists(ClaudeLocal("api")));
    }

    [Fact]
    public void Apply_creates_the_project_folder_the_index_entry_and_the_claude_local_block()
    {
        _t.Service.Create(_t.VaultDir);
        var (scan, repo) = _t.Repo("api");

        var r = _t.Service.Enable(scan, repo, apply: true);

        Assert.Equal(EnableOutcome.Ok, r.Outcome);
        Assert.True(r.Applied);
        foreach (var folder in VaultFiles.ProjectFolders) Assert.True(Directory.Exists(Path.Combine(_t.VaultDir, "api", folder)));
        Assert.Contains("id=api -->", File.ReadAllText(Path.Combine(_t.VaultDir, "api", VaultFiles.ProjectIndex)));
        Assert.Contains("- [api](api/_index.md)", File.ReadAllText(Path.Combine(_t.VaultDir, VaultFiles.IndexMain)));

        var local = File.ReadAllText(ClaudeLocal("api"));
        Assert.Contains(VaultService.BlockStart, local);
        Assert.Contains(Path.Combine(_t.VaultDir, VaultFiles.IndexMain), local);
        Assert.Contains("api", _t.Service.GetStatus().Projects.Select(p => p.Name));
        Assert.Contains("api", _t.Service.GetStatus().Projects.Select(p => p.RepoId));
    }

    [Fact]
    public void Apply_keeps_what_the_user_wrote_and_is_idempotent()
    {
        _t.Service.Create(_t.VaultDir);
        var (scan, repo) = _t.Repo("api");
        File.WriteAllText(ClaudeLocal("api"), "# My notes\n\nkeep me\n");

        _t.Service.Enable(scan, repo, apply: true);
        var first = File.ReadAllText(ClaudeLocal("api"));
        Assert.StartsWith("# My notes\n\nkeep me\n", first);

        var again = _t.Service.Enable(scan, repo, apply: true);
        Assert.Equal(EnableOutcome.Ok, again.Outcome);
        Assert.Equal(first, File.ReadAllText(ClaudeLocal("api")));
        Assert.Equal(1, first.Split(VaultService.BlockStart).Length - 1);
    }

    [Fact]
    public void Existing_project_files_are_never_overwritten()
    {
        _t.Service.Create(_t.VaultDir);
        var (scan, repo) = _t.Repo("api");
        _t.Service.Enable(scan, repo, apply: true);
        var index = Path.Combine(_t.VaultDir, "api", VaultFiles.ProjectIndex);
        var edited = File.ReadAllText(index) + "\n- [page](memory/page.md) - my page\n";
        File.WriteAllText(index, edited);

        _t.Service.Enable(scan, repo, apply: true);

        Assert.Equal(edited, File.ReadAllText(index));
    }

    [Fact]
    public void Another_repo_with_the_same_folder_name_is_refused()
    {
        _t.Service.Create(_t.VaultDir);
        var (scan1, one) = _t.Repo("team-a/api");
        var (scan2, two) = _t.Repo("team-b/api");
        Assert.Equal(EnableOutcome.Ok, _t.Service.Enable(scan1, one, apply: true).Outcome);
        Assert.Equal(EnableOutcome.NameTaken, _t.Service.Enable(scan2, two, apply: true).Outcome);
        Assert.False(File.Exists(ClaudeLocal("team-b/api")));
    }

    [Fact]
    public void Damaged_markers_in_claude_local_are_not_touched()
    {
        _t.Service.Create(_t.VaultDir);
        var (scan, repo) = _t.Repo("api");
        File.WriteAllText(ClaudeLocal("api"), VaultService.BlockStart + "\nhalf a block\n");

        Assert.Equal(EnableOutcome.BlockDamaged, _t.Service.Enable(scan, repo, apply: true).Outcome);
        Assert.Equal(VaultService.BlockStart + "\nhalf a block\n", File.ReadAllText(ClaudeLocal("api")));
    }

    [Fact]
    public void A_claude_local_link_leading_out_of_the_repo_is_refused()
    {
        if (OperatingSystem.IsWindows()) return; // symlinks need extra rights there
        _t.Service.Create(_t.VaultDir);
        var (scan, repo) = _t.Repo("api");
        var outside = Path.Combine(_t.Root, "toolkit-file.md");
        File.WriteAllText(outside, "toolkit");
        File.CreateSymbolicLink(ClaudeLocal("api"), outside);

        Assert.Equal(EnableOutcome.Forbidden, _t.Service.Enable(scan, repo, apply: true).Outcome);
        Assert.Equal("toolkit", File.ReadAllText(outside));
    }

    [Fact]
    public void Moving_the_vault_and_enabling_again_refreshes_the_block_path()
    {
        _t.Service.Create(_t.VaultDir);
        var (scan, repo) = _t.Repo("api");
        _t.Service.Enable(scan, repo, apply: true);

        var moved = Path.Combine(_t.Root, "moved");
        Directory.Move(_t.VaultDir, moved);
        _t.Service.Relink(moved);
        _t.Service.Enable(scan, repo, apply: true);

        var local = File.ReadAllText(ClaudeLocal("api"));
        Assert.Contains(moved, local);
        Assert.DoesNotContain(_t.VaultDir + Path.DirectorySeparatorChar, local);
        Assert.Equal(1, local.Split(VaultService.BlockStart).Length - 1);
    }
}

public class SecondBrainSkillTests : IDisposable
{
    private readonly string _claude = Path.Combine(Path.GetTempPath(), "radar-skill-" + Guid.NewGuid().ToString("N"));
    private SecondBrainSkill Skill => new(_claude);
    public void Dispose() { try { Directory.Delete(_claude, true); } catch { /* best effort */ } }

    [Fact]
    public void Installs_the_bundled_skill_with_a_marker_and_then_reports_up_to_date()
    {
        Assert.Equal(SkillState.NotInstalled, Skill.Status().State);

        Assert.Equal(SkillOutcome.Installed, Skill.Install(update: false));

        var text = File.ReadAllText(Skill.FilePath);
        Assert.Equal(SecondBrainSkill.Render(), text);
        Assert.Contains("name: radar-second-brain", text);
        Assert.Matches(@"<!-- radar-skill v1 sha256:[0-9a-f]{64} -->\n$", text);
        Assert.Equal(SkillState.UpToDate, Skill.Status().State);
        Assert.Equal(SkillOutcome.AlreadyInstalled, Skill.Install(update: false));
    }

    [Fact]
    public void A_skill_edited_by_hand_is_never_overwritten()
    {
        Skill.Install(false);
        File.AppendAllText(Skill.FilePath, "\nmy own rule\n");
        var before = File.ReadAllText(Skill.FilePath);

        Assert.Equal(SkillState.Modified, Skill.Status().State);
        Assert.Equal(SkillOutcome.Modified, Skill.Install(update: true));
        Assert.Equal(before, File.ReadAllText(Skill.FilePath));
    }

    [Fact]
    public void An_edit_inside_the_body_counts_as_modified_too()
    {
        Skill.Install(false);
        File.WriteAllText(Skill.FilePath, File.ReadAllText(Skill.FilePath).Replace("Second brain", "My brain"));

        Assert.Equal(SkillState.Modified, Skill.Status().State);
        Assert.Equal(SkillOutcome.Modified, Skill.Install(update: true));
    }

    [Fact]
    public void A_different_skill_with_the_same_name_is_left_alone()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Skill.FilePath)!);
        File.WriteAllText(Skill.FilePath, "---\nname: radar-second-brain\n---\nmine\n");

        Assert.Equal(SkillState.Foreign, Skill.Status().State);
        Assert.Equal(SkillOutcome.Foreign, Skill.Install(update: true));
        Assert.Equal("---\nname: radar-second-brain\n---\nmine\n", File.ReadAllText(Skill.FilePath));
    }

    [Fact]
    public void A_symlinked_skill_or_skills_folder_is_left_alone()
    {
        if (OperatingSystem.IsWindows()) return;
        var toolkit = Path.Combine(_claude, "toolkit-skills");
        Directory.CreateDirectory(toolkit);
        File.CreateSymbolicLink(Path.Combine(_claude, "skills"), toolkit);

        Assert.Equal(SkillState.Linked, Skill.Status().State);
        Assert.Equal(SkillOutcome.Linked, Skill.Install(update: true));
        Assert.Empty(Directory.GetFileSystemEntries(toolkit));
    }

    [Fact]
    public void An_untouched_older_copy_is_updated_only_after_confirmation()
    {
        var path = Skill.FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string oldBody = "---\nname: radar-second-brain\n---\nold version\n";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(oldBody)));
        File.WriteAllText(path, oldBody + $"<!-- radar-skill v0 sha256:{hash} -->\n");

        var status = Skill.Status();
        Assert.Equal(SkillState.Outdated, status.State);
        Assert.Equal(0, status.InstalledVersion);

        Assert.Equal(SkillOutcome.UpdateNeeded, Skill.Install(update: false));
        Assert.Contains("old version", File.ReadAllText(path));

        Assert.Equal(SkillOutcome.Updated, Skill.Install(update: true));
        Assert.Equal(SecondBrainSkill.Render(), File.ReadAllText(path));
    }
}

public class VaultEndpointTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "radar-vault-api-" + Guid.NewGuid().ToString("N"));
    private string Repos => Path.Combine(_root, "repos");
    private string VaultDir => Path.Combine(_root, "vault");
    private readonly ServerFactory _f = new();

    public void Dispose()
    {
        _f.Dispose();
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private async Task ScanAsync(HttpClient c)
    {
        Directory.CreateDirectory(Path.Combine(Repos, "orders-api", ".git"));
        await c.PutAsJsonAsync("/api/settings", new { scanPath = Repos });
        var id = (await (await c.PostAsync("/api/scans", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        using var resp = await c.GetAsync($"/api/scans/{id}/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (await reader.ReadLineAsync(cts.Token) is not null) { }
    }

    [Fact]
    public async Task Requires_the_session_token()
    {
        using var anonymous = _f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/vault")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/vault/skill", null)).StatusCode);
    }

    [Fact]
    public async Task Vault_lifecycle_over_the_api()
    {
        var c = await _f.AuthedClientAsync();

        var none = await c.GetFromJsonAsync<JsonElement>("/api/vault");
        Assert.Equal("none", none.GetProperty("state").GetString());
        Assert.False(string.IsNullOrEmpty(none.GetProperty("suggestedPath").GetString()));

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/vault", new { path = "" })).StatusCode);

        var created = await c.PostAsJsonAsync("/api/vault", new { path = VaultDir });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("ok", (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());
        var again = await c.PostAsJsonAsync("/api/vault", new { path = VaultDir });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already-vault", (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var moved = Path.Combine(_root, "moved");
        Directory.Move(VaultDir, moved);
        var missing = await c.GetFromJsonAsync<JsonElement>("/api/vault");
        Assert.Equal("missing", missing.GetProperty("state").GetString());
        Assert.Equal("folder-missing", missing.GetProperty("reason").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/vault", new { path = Path.Combine(_root, "nope") })).StatusCode);
        var relinked = await c.PutAsJsonAsync("/api/vault", new { path = moved });
        Assert.Equal(HttpStatusCode.OK, relinked.StatusCode);
        Assert.Equal("ok", (await c.GetFromJsonAsync<JsonElement>("/api/vault")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Error_texts_follow_the_accept_language_header()
    {
        var c = await _f.AuthedClientAsync();
        Directory.CreateDirectory(VaultDir);
        File.WriteAllText(Path.Combine(VaultDir, "x.md"), "x");

        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        var en = await (await c.PostAsJsonAsync("/api/vault", new { path = VaultDir })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("not empty", en.GetProperty("error").GetString());
        Assert.Equal("not-empty", en.GetProperty("code").GetString());

        c.DefaultRequestHeaders.AcceptLanguage.Clear();
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("pl");
        var pl = await (await c.PostAsJsonAsync("/api/vault", new { path = VaultDir })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("nie jest pusty", pl.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Skill_is_installed_into_the_configured_claude_dir_only_when_asked()
    {
        var c = await _f.AuthedClientAsync();
        var file = Path.Combine(_f.ClaudeDir, "skills", "radar-second-brain", "SKILL.md");

        var before = await c.GetFromJsonAsync<JsonElement>("/api/vault/skill");
        Assert.Equal("not-installed", before.GetProperty("state").GetString());
        Assert.False(File.Exists(file));

        var installed = await c.PostAsync("/api/vault/skill", null);
        Assert.Equal(HttpStatusCode.OK, installed.StatusCode);
        Assert.Equal("up-to-date", (await installed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("skill").GetProperty("state").GetString());
        Assert.True(File.Exists(file));

        File.AppendAllText(file, "\nedited\n");
        var refused = await c.PostAsJsonAsync("/api/vault/skill", new { update = true });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("edited", File.ReadAllText(file));
    }

    [Fact]
    public async Task Enabling_a_project_previews_first_and_writes_only_after_confirmation()
    {
        var c = await _f.AuthedClientAsync();
        await ScanAsync(c);

        var noVault = await c.PostAsJsonAsync("/api/vault/projects", new { repoId = "orders-api" });
        Assert.Equal(HttpStatusCode.Conflict, noVault.StatusCode);

        await c.PostAsJsonAsync("/api/vault", new { path = VaultDir });
        var local = Path.Combine(Repos, "orders-api", "CLAUDE.local.md");

        var preview = await c.PostAsJsonAsync("/api/vault/projects", new { repoId = "orders-api" });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var plan = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(plan.GetProperty("applied").GetBoolean());
        Assert.Contains(VaultDir, plan.GetProperty("plan").GetProperty("block").GetString() ?? "");
        Assert.False(File.Exists(local));
        Assert.False(Directory.Exists(Path.Combine(VaultDir, "orders-api")));

        var applied = await c.PostAsJsonAsync("/api/vault/projects", new { repoId = "orders-api", confirm = true });
        Assert.Equal(HttpStatusCode.Created, applied.StatusCode);
        Assert.True(File.Exists(local));
        Assert.True(Directory.Exists(Path.Combine(VaultDir, "orders-api", "memory")));
        var status = await c.GetFromJsonAsync<JsonElement>("/api/vault");
        Assert.Equal("orders-api", status.GetProperty("projects")[0].GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/api/vault/projects", new { repoId = "ghost", confirm = true })).StatusCode);
    }
}
