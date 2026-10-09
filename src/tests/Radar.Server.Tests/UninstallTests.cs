using Radar.Server.Features.Uninstall;
using Radar.Server.Features.Vault;

namespace Radar.Server.Tests;

public sealed class UninstallTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "radar-uninstall-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(_tmp, ".radar");
    private string App => Path.Combine(Root, "app");
    private string Bin => Path.Combine(_tmp, "bin");
    private string Data => Path.Combine(_tmp, "data");
    private string Claude => Path.Combine(_tmp, "claude");

    public UninstallTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { }
    }

    private void FakeInstall(bool windows = false)
    {
        Directory.CreateDirectory(Path.Combine(App, "wwwroot"));
        File.WriteAllText(Path.Combine(App, "wwwroot", "index.html"), "<html/>");
        File.WriteAllText(Path.Combine(App, windows ? "radar.exe" : "radar"), "x");
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "settings.json"), "{}");
    }

    private void FakeLink()
    {
        Directory.CreateDirectory(Bin);
        File.CreateSymbolicLink(Path.Combine(Bin, "radar"), Path.Combine(App, "radar"));
    }

    private UninstallEnvironment Env(bool windows = false, Func<string?>? read = null, Action<string>? write = null, Action<string>? del = null) =>
        new(App, Bin, Data, new SecondBrainSkill(Claude), windows, read, write, del);

    [Fact]
    public void A_plan_deletes_nothing_and_lists_the_link_and_the_program()
    {
        FakeInstall(); FakeLink();
        var plan = new Uninstaller(Env()).Plan(false, false);

        Assert.Equal([UninstallKind.Link, UninstallKind.App], plan.Items.Select(i => i.Kind));
        Assert.True(Directory.Exists(App));
        Assert.True(File.Exists(Path.Combine(Bin, "radar")) || new FileInfo(Path.Combine(Bin, "radar")).LinkTarget is not null);
    }

    [Fact]
    public void Applying_removes_the_program_the_link_and_the_empty_root_but_keeps_settings()
    {
        FakeInstall(); FakeLink();
        var u = new Uninstaller(Env());

        Assert.Empty(u.Apply(u.Plan(false, false)));

        Assert.False(Directory.Exists(Root));
        Assert.Null(new FileInfo(Path.Combine(Bin, "radar")).LinkTarget);
        Assert.True(Directory.Exists(Data));
    }

    [Fact]
    public void The_root_is_kept_when_something_else_lives_in_it()
    {
        FakeInstall();
        File.WriteAllText(Path.Combine(Root, "mine.txt"), "keep");
        var u = new Uninstaller(Env());

        u.Apply(u.Plan(false, false));

        Assert.False(Directory.Exists(App));
        Assert.True(File.Exists(Path.Combine(Root, "mine.txt")));
    }

    [Fact]
    public void Settings_and_the_skill_go_only_when_asked()
    {
        FakeInstall();
        new SecondBrainSkill(Claude).Install(false);
        var u = new Uninstaller(Env());

        var plan = u.Plan(true, true);
        Assert.Contains(plan.Items, i => i.Kind == UninstallKind.Data);
        Assert.Contains(plan.Items, i => i.Kind == UninstallKind.Skill);
        Assert.Empty(u.Apply(plan));

        Assert.False(Directory.Exists(Data));
        Assert.False(Directory.Exists(Path.Combine(Claude, "skills", SecondBrainSkill.Name)));
        Assert.True(Directory.Exists(Path.Combine(Claude, "skills")));
    }

    [Fact]
    public void An_edited_skill_is_left_alone()
    {
        FakeInstall();
        var skill = new SecondBrainSkill(Claude);
        skill.Install(false);
        File.AppendAllText(skill.FilePath, "\nmy own notes\n");

        var plan = new Uninstaller(Env()).Plan(false, true);

        Assert.DoesNotContain(plan.Items, i => i.Kind == UninstallKind.Skill);
        Assert.Contains(plan.Skipped, s => s.Contains(SecondBrainSkill.Name));
    }

    [Fact]
    public void A_build_output_without_wwwroot_is_not_touched()
    {
        Directory.CreateDirectory(App);
        File.WriteAllText(Path.Combine(App, "radar"), "x");

        var plan = new Uninstaller(Env()).Plan(false, false);

        Assert.Empty(plan.Items);
        Assert.Single(plan.Skipped);
    }

    [Fact]
    public void A_copy_inside_a_git_repository_is_not_touched()
    {
        Directory.CreateDirectory(Path.Combine(Root, ".git"));
        FakeInstall();

        var plan = new Uninstaller(Env()).Plan(false, false);

        Assert.Empty(plan.Items);
        Assert.Contains(plan.Skipped, s => s.Contains("git repository"));
    }

    [Fact]
    public void A_link_that_points_elsewhere_is_left_alone()
    {
        FakeInstall();
        Directory.CreateDirectory(Bin);
        var other = Path.Combine(_tmp, "other-radar");
        File.WriteAllText(other, "x");
        File.CreateSymbolicLink(Path.Combine(Bin, "radar"), other);

        var plan = new Uninstaller(Env()).Plan(false, false);

        Assert.DoesNotContain(plan.Items, i => i.Kind == UninstallKind.Link);
        Assert.Contains(plan.Skipped, s => s.Contains("points to"));
    }

    [Fact]
    public void On_windows_the_path_entry_is_removed_and_the_folder_is_handed_to_the_helper()
    {
        FakeInstall(windows: true);
        var path = $@"C:\Tools;{App}{Path.DirectorySeparatorChar};D:\More";
        string? written = null;
        string? deleted = null;
        var u = new Uninstaller(Env(windows: true, read: () => path, write: p => written = p, del: d => deleted = d));

        Assert.Empty(u.Apply(u.Plan(false, false)));

        Assert.Equal(@"C:\Tools;D:\More", written);
        Assert.Equal(App, deleted);
        Assert.True(Directory.Exists(App));
    }

    [Fact]
    public void The_command_shows_a_plan_without_confirm_and_deletes_with_it()
    {
        FakeInstall(); FakeLink();

        var dry = new StringWriter();
        Assert.Equal(0, UninstallCommand.Run(Env(), false, false, false, dry));
        Assert.Contains("Nothing was deleted", dry.ToString());
        Assert.True(Directory.Exists(App));

        var real = new StringWriter();
        Assert.Equal(0, UninstallCommand.Run(Env(), false, false, true, real));
        Assert.Contains("Done.", real.ToString());
        Assert.Contains("second-brain vault", real.ToString());
        Assert.False(Directory.Exists(App));
    }

    [Fact]
    public void An_unknown_option_is_a_usage_error()
    {
        var o = new StringWriter();
        Assert.Equal(2, UninstallCommand.Run(["--wipe"], o));
        Assert.Contains("Usage:", o.ToString());
    }
}
