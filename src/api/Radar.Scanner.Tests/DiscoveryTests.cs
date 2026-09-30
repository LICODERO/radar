using Radar.Scanner;

namespace Radar.Scanner.Tests;

public class DiscoveryTests
{
    private static List<string> Names(FixtureTree t, int depth = 4) =>
        RepoDiscovery.Discover(t.Root, depth).Select(d => Path.GetRelativePath(t.Root, d).Replace('\\', '/')).ToList();

    [Fact]
    public void Finds_repos_recursively_and_sorted()
    {
        using var t = new FixtureTree();
        t.Repo("b-repo");
        t.Repo("a-repo");
        t.Repo("group/nested-repo");
        Assert.Equal(["a-repo", "b-repo", "group/nested-repo"], Names(t).OrderBy(x => x, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void Does_not_descend_into_a_found_repo()
    {
        using var t = new FixtureTree();
        t.Repo("outer");
        t.Repo("outer/inner");
        Assert.Equal(["outer"], Names(t));
    }

    [Fact]
    public void Skips_ignored_and_hidden_directories()
    {
        using var t = new FixtureTree();
        t.Repo("node_modules/pkg");
        t.Repo("bin/x");
        t.Repo(".hidden/x");
        t.Repo("real");
        Assert.Equal(["real"], Names(t));
    }

    [Fact]
    public void Respects_max_depth()
    {
        using var t = new FixtureTree();
        t.Repo("a/b/c/d/deep");
        t.Repo("a/shallow");
        Assert.Equal(["a/shallow"], Names(t, 2));
        Assert.Contains("a/b/c/d/deep", Names(t, 5));
    }

    [Fact]
    public void Treats_git_file_as_repo_marker()
    {
        using var t = new FixtureTree();
        t.Dir("worktree");
        t.File("worktree/.git", "gitdir: /somewhere");
        Assert.Equal(["worktree"], Names(t));
    }

    [Fact]
    public void Root_that_is_a_repo_is_the_only_result()
    {
        using var t = new FixtureTree();
        t.Repo(".");
        t.Repo("child");
        var found = RepoDiscovery.Discover(t.Root, 4).ToList();
        Assert.Single(found);
    }

    [Fact]
    public void Does_not_follow_directory_symlinks()
    {
        if (OperatingSystem.IsWindows()) return;
        using var outside = new FixtureTree();
        outside.Repo("secret-repo");
        using var t = new FixtureTree();
        Directory.CreateSymbolicLink(t.Path_("link"), outside.Root);
        t.Repo("real");
        Assert.Equal(["real"], Names(t));
    }

    [Fact]
    public void Missing_root_yields_nothing()
    {
        Assert.Empty(RepoDiscovery.Discover(Path.Combine(Path.GetTempPath(), "radar-missing-" + Guid.NewGuid()), 4));
    }
}
