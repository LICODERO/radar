using Radar.Scanner;
using Radar.Server.Features.Gaps;

namespace Radar.Server.Tests;

public class GapCommandTests
{
    private static RepoInfo Repo(string[] gaps, QualityInfo? q) =>
        new("shop", "shop", "shop", "SH", ".NET", [".NET"], new ClaudeMdInfo(true, "CLAUDE.md"), [], [], new CoverageInfo(40, new CoverageParts(40, 0, 0)), gaps, q);

    private static ScanResult Result(RepoInfo repo) =>
        new(ScanResult.CurrentSchemaVersion, "/p", DateTimeOffset.Now, 0, new SummaryInfo(1, 0, 0, 0, 0, [], 40), [repo], [], [], []);

    [Fact]
    public void The_weak_files_prompt_lists_the_findings_per_file_in_plain_english()
    {
        var q = new QualityInfo(40, [new FileQuality("CLAUDE.md", FileKinds.ClaudeMd, 40)],
        [
            new QualityFinding("CLAUDE.md", "claude-md-thin", Severities.Error, "2"),
            new QualityFinding("CLAUDE.md", "claude-md-broken-ref", Severities.Warning, "docs/gone.md")
        ]);
        var item = GapCommands.Build(Result(Repo([GapTypes.WeakFiles], q))).Single();

        Assert.Equal(GapTypes.WeakFiles, item.Type);
        Assert.Contains("- CLAUDE.md: only 2 non-empty lines", item.Prompt);
        Assert.Contains("refers to paths that do not exist: docs/gone.md", item.Prompt);
        Assert.Contains("do not touch files that are not listed", item.Prompt);
    }

    [Fact]
    public void Minor_notes_come_after_errors_and_warnings_when_the_prompt_is_capped()
    {
        var findings = Enumerable.Range(0, 40).Select(i => new QualityFinding($".claude/agents/a{i}.md", "agent-no-tools", Severities.Info))
            .Append(new QualityFinding("CLAUDE.md", "claude-md-thin", Severities.Error, "1")).ToList();
        var prompt = GapCommands.PromptFor(GapTypes.WeakFiles, [], findings);

        Assert.Contains("- CLAUDE.md:", prompt);
        Assert.Equal(30, prompt.Split('\n').Count(l => l.StartsWith("- ")));
    }

    [Fact]
    public void Every_finding_code_the_scanner_can_emit_has_a_prompt_text()
    {
        foreach (var code in new[] { "claude-md-thin", "claude-md-too-long", "claude-md-no-commands", "claude-md-no-structure", "claude-md-placeholder", "claude-md-broken-ref",
            "agent-no-frontmatter", "agent-no-name", "agent-name-mismatch", "agent-no-description", "agent-short-description", "agent-no-tools", "agent-thin-prompt", "agent-duplicate-name",
            "skill-no-frontmatter", "skill-name-mismatch", "skill-no-description", "skill-short-description", "skill-thin-body" })
            Assert.NotEqual(code, QualityText.Describe(new QualityFinding("p", code, Severities.Info, "x")));
    }
}
