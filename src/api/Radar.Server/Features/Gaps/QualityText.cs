using Radar.Scanner;

namespace Radar.Server.Features.Gaps;

/// <summary>English wording of quality findings for the prompts sent to the AI tools (the UI has its own PL/EN texts per code).</summary>
public static class QualityText
{
    public static string Describe(QualityFinding f) => f.Code switch
    {
        "claude-md-thin" => $"only {f.Detail} non-empty lines, too little to guide an AI",
        "claude-md-too-long" => $"{f.Detail} lines, long files are followed less reliably (trim to the essentials)",
        "claude-md-no-commands" => "no build, test or run commands",
        "claude-md-no-structure" => "no headings, add structure",
        "claude-md-placeholder" => "contains placeholder text that was never filled in",
        "claude-md-stale" => $"not changed for {f.Detail} days while the repository kept changing, check that it is still true",
        "claude-md-broken-ref" => $"refers to paths that do not exist: {f.Detail}",
        "agent-no-frontmatter" => "no valid frontmatter (name, description, tools)",
        "agent-no-name" => "frontmatter has no name",
        "agent-name-mismatch" => $"name '{f.Detail}' differs from the file name",
        "agent-no-description" => "no description, so Claude cannot tell when to delegate",
        "agent-short-description" => $"description is only {f.Detail} characters, too vague to delegate by",
        "agent-no-tools" => "no tools list (inherits every tool), restrict it to what the agent needs",
        "agent-thin-prompt" => "the instructions are almost empty",
        "agent-duplicate-name" => $"another agent is also called '{f.Detail}'",
        "skill-no-frontmatter" => "no valid frontmatter (name, description)",
        "skill-name-mismatch" => $"name '{f.Detail}' differs from the folder name",
        "skill-no-description" => "no description, so the skill will never be picked",
        "skill-short-description" => $"description is only {f.Detail} characters, too vague to trigger on",
        "skill-thin-body" => "the steps are almost empty",
        _ => f.Code
    };
}
