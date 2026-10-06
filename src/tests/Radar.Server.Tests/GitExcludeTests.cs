using System.Diagnostics;
using Radar.Server.Features.Visibility;

namespace Radar.Server.Tests;

public class GitExcludeTests
{
    [Fact]
    public void Entry_is_anchored_escaped_and_folders_get_a_slash()
    {
        Assert.Equal("/.claude/agents/a.md", GitExclude.Entry(".claude/agents/a.md", false));
        Assert.Equal("/.claude/skills/x/", GitExclude.Entry(".claude/skills/x", true));
        Assert.Equal("/.claude/agents/a\\[1].md", GitExclude.Entry(".claude/agents/a[1].md", false));
        Assert.Equal("/.claude/agents/a\\*.md", GitExclude.Entry(".claude\\agents\\a*.md", false));
    }

    [Fact]
    public void Add_creates_the_block_keeps_foreign_lines_and_is_idempotent()
    {
        var once = GitExclude.Add("# mine\n*.log\n", "/.claude/agents/a.md")!;
        Assert.StartsWith("# mine\n*.log\n", once);
        Assert.Equal(["/.claude/agents/a.md"], GitExclude.Entries(once));
        Assert.Equal(once, GitExclude.Add(once, "/.claude/agents/a.md"));
        var two = GitExclude.Add(once, "/.claude/skills/s/")!;
        Assert.Equal(["/.claude/agents/a.md", "/.claude/skills/s/"], GitExclude.Entries(two));
    }

    [Fact]
    public void Removing_the_last_entry_removes_the_block_and_nothing_else()
    {
        var text = GitExclude.Add("# mine\n*.log\n", "/.claude/agents/a.md")!;
        var back = GitExclude.Remove(text, "/.claude/agents/a.md");
        Assert.Equal("# mine\n*.log\n", back);
        Assert.Equal("", GitExclude.Remove(GitExclude.Add(null, "/x"), "/x"));
    }

    [Fact]
    public void Damaged_markers_are_never_rewritten()
    {
        var damaged = $"*.log\n{GitExclude.Start}\n/a\n";
        Assert.Null(GitExclude.Add(damaged, "/b"));
        Assert.Null(GitExclude.Remove(damaged, "/a"));
    }

    [Fact]
    public void ResolveFile_finds_the_exclude_of_a_plain_repo_and_of_a_worktree()
    {
        var root = Path.Combine(Path.GetTempPath(), "radar-excl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var repo = Path.Combine(root, "main");
            Directory.CreateDirectory(repo);
            Run(repo, "init", "-q");
            Run(repo, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "--allow-empty", "-m", "i");
            var wt = Path.Combine(root, "wt");
            Run(repo, "worktree", "add", "-q", wt);

            // the temp dir may sit behind a symlink (/var -> /private/var on macOS), so compare the tail
            var tail = Path.Combine("main", ".git", "info", "exclude");
            Assert.EndsWith(tail, GitExclude.ResolveFile(repo));
            Assert.EndsWith(tail, GitExclude.ResolveFile(wt));
            Assert.Null(GitExclude.ResolveFile(root));
        }
        finally { try { Directory.Delete(root, true); } catch { /* best effort */ } }
    }

    private static void Run(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(dir);
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }
}
