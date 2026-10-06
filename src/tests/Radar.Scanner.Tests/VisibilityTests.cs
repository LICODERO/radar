using System.Diagnostics;

namespace Radar.Scanner.Tests;

public class VisibilityTests
{
    private static void Git(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-C", dir, "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false" }.Concat(args)) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)} failed");
    }

    private static string RealRepo(FixtureTree t, string name)
    {
        var dir = t.Dir(name);
        Git(dir, "init", "-q");
        return dir;
    }

    [Fact]
    public void Tracked_files_are_public_ignored_private_and_the_rest_untracked()
    {
        using var t = new FixtureTree();
        var repo = RealRepo(t, "app");
        t.File("app/.claude/agents/shared.md", "---\nname: shared\n---\nx");
        t.File("app/.claude/agents/mine.md", "---\nname: mine\n---\nx");
        t.File("app/.claude/agents/new.md", "---\nname: new\n---\nx");
        t.File("app/.claude/skills/pub/SKILL.md", "---\nname: pub\n---\nx");
        t.File("app/.claude/skills/priv/SKILL.md", "---\nname: priv\n---\nx");
        t.File("app/.claude/workflows/flow.md", "---\nname: flow\n---\nx");
        Git(repo, "add", ".claude/agents/shared.md", ".claude/skills/pub", ".claude/workflows");
        Git(repo, "commit", "-q", "-m", "init");
        File.WriteAllText(Path.Combine(repo, ".git", "info", "exclude"), "/.claude/agents/mine.md\n/.claude/skills/priv/\n");

        var a = RepoAnalyzer.Analyze(t.Root, repo).Repo;
        Assert.Equal(Visibilities.Public, a.Agents.Single(x => x.Name == "shared").Visibility);
        Assert.Equal(Visibilities.Private, a.Agents.Single(x => x.Name == "mine").Visibility);
        Assert.Equal(Visibilities.Untracked, a.Agents.Single(x => x.Name == "new").Visibility);
        Assert.Equal(Visibilities.Public, a.Skills.Single(x => x.Name == "pub").Visibility);
        Assert.Equal(Visibilities.Private, a.Skills.Single(x => x.Name == "priv").Visibility);

        var raw = RepoAnalyzer.Analyze(t.Root, repo).Workflows.Single();
        Assert.Equal(Visibilities.Public, raw.Visibility);
    }

    [Fact]
    public void A_skill_with_only_one_tracked_file_counts_as_public()
    {
        using var t = new FixtureTree();
        var repo = RealRepo(t, "app");
        t.File("app/.claude/skills/s/SKILL.md", "---\nname: s\n---\nx");
        t.File("app/.claude/skills/s/ref.md", "y");
        Git(repo, "add", ".claude/skills/s/ref.md");
        Git(repo, "commit", "-q", "-m", "init");
        Assert.Equal(Visibilities.Public, RepoAnalyzer.Analyze(t.Root, repo).Repo.Skills.Single().Visibility);
    }

    [Fact]
    public void A_fake_git_dir_gives_unknown_and_never_throws()
    {
        using var t = new FixtureTree();
        var repo = t.Repo("fake");
        t.File("fake/.claude/agents/a.md", "---\nname: a\n---\nx");
        Assert.Equal(Visibilities.Unknown, RepoAnalyzer.Analyze(t.Root, repo).Repo.Agents.Single().Visibility);
    }

    [Fact]
    public void A_repo_without_items_does_not_call_git()
    {
        using var t = new FixtureTree();
        var repo = t.Repo("empty");
        Assert.Empty(GitVisibility.Resolve(repo, []));
    }

    private static string Agent(string name) => $"---\nname: {name}\ndescription: Does {name} work carefully\n---\nBody of the agent {name}.";

    [Fact]
    public void A_public_and_a_private_item_with_the_same_name_are_a_conflict()
    {
        using var t = new FixtureTree();
        var repo = RealRepo(t, "app");
        t.File("app/.claude/agents/code-reviewer.md", Agent("Reviewer"));          // committed: public
        t.File("app/.claude/agents/mine.md", Agent("reviewer"));                    // hidden: private (same name, other case)
        t.File("app/.claude/skills/deploy/SKILL.md", "---\nname: deploy\ndescription: Deploys it\n---\nx");
        t.File("app/.claude/skills/deploy-local/SKILL.md", "---\nname: deploy\ndescription: Deploys it locally\n---\nx");
        t.File("app/.claude/workflows/ship.md", "---\nname: ship\n---\nx");
        Git(repo, "add", ".claude/agents/code-reviewer.md", ".claude/skills/deploy", ".claude/workflows");
        Git(repo, "commit", "-q", "-m", "init");
        File.WriteAllText(Path.Combine(repo, ".git", "info", "exclude"), "/.claude/agents/mine.md\n/.claude/skills/deploy-local/\n");

        var c = RepoAnalyzer.Analyze(t.Root, repo).Repo.NameConflicts!;
        Assert.Equal(["agent", "skill"], c.Select(x => x.Kind).ToArray());
        var agent = c.Single(x => x.Kind == "agent");
        Assert.Equal([".claude/agents/code-reviewer.md", ".claude/agents/mine.md"], agent.Items.Select(i => i.Path).ToArray());
        Assert.Equal([Visibilities.Public, Visibilities.Private], agent.Items.Select(i => i.Visibility).ToArray());
        Assert.Equal([".claude/skills/deploy/SKILL.md", ".claude/skills/deploy-local/SKILL.md"], c.Single(x => x.Kind == "skill").Items.Select(i => i.Path).ToArray());
    }

    [Fact]
    public void An_untracked_item_counts_as_local_and_a_workflow_can_conflict_too()
    {
        using var t = new FixtureTree();
        var repo = RealRepo(t, "app");
        t.File("app/.claude/workflows/ship.md", "---\nname: ship\n---\nx");
        t.File("app/.claude/workflows/ship-new.md", "---\nname: ship\n---\nx");
        Git(repo, "add", ".claude/workflows/ship.md");
        Git(repo, "commit", "-q", "-m", "init");
        var c = RepoAnalyzer.Analyze(t.Root, repo).Repo.NameConflicts!;
        var wf = Assert.Single(c);
        Assert.Equal("workflow", wf.Kind);
        Assert.Equal([Visibilities.Public, Visibilities.Untracked], wf.Items.Select(i => i.Visibility).ToArray());
    }

    [Fact]
    public void Duplicates_with_the_same_visibility_or_unknown_visibility_are_not_conflicts()
    {
        using var t = new FixtureTree();
        var shared = RealRepo(t, "shared");
        t.File("shared/.claude/agents/a.md", Agent("twin"));
        t.File("shared/.claude/agents/b.md", Agent("twin"));
        Git(shared, "add", ".claude/agents");
        Git(shared, "commit", "-q", "-m", "init");
        Assert.Empty(RepoAnalyzer.Analyze(t.Root, shared).Repo.NameConflicts!);

        var fake = t.Repo("fake");
        t.File("fake/.claude/agents/a.md", Agent("twin"));
        t.File("fake/.claude/agents/b.md", Agent("twin"));
        Assert.Empty(RepoAnalyzer.Analyze(t.Root, fake).Repo.NameConflicts!);
    }

    [Fact]
    public void The_aggregated_workflow_keeps_the_visibility_per_repo()
    {
        using var t = new FixtureTree();
        var repo = RealRepo(t, "app");
        t.File("app/.claude/workflows/flow.md", "---\nname: flow\n---\nx");
        var a = RepoAnalyzer.Analyze(t.Root, repo);
        var wf = WorkflowAggregator.Aggregate([a.Repo], a.Workflows).Single();
        Assert.Equal(Visibilities.Untracked, wf.Repos.Single().Visibility);
    }
}

public class LocalLinkTests
{
    [Fact]
    public void A_workflow_named_in_CLAUDE_local_md_counts_as_linked()
    {
        using var t = new FixtureTree();
        var repo = t.Repo("app");
        t.File("app/CLAUDE.md", "# App\n");
        t.File("app/CLAUDE.local.md", "- `scratch` File: `.claude/workflows/scratch.md`\n");
        t.File("app/.claude/workflows/scratch.md", "---\nname: scratch\n---\nx");
        t.File("app/.claude/workflows/other.md", "---\nname: other\n---\nx");
        var a = RepoAnalyzer.Analyze(t.Root, repo);
        Assert.True(a.Workflows.Single(w => w.Name == "scratch").Linked);
        Assert.False(a.Workflows.Single(w => w.Name == "other").Linked);
    }
}
