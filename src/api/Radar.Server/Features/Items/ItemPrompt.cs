using Radar.Server.Features.Agents;

namespace Radar.Server.Features.Items;

/// <summary>What is sent to Claude when drafting an agent, a skill or a workflow: the user's description, the stack name and the names already in the repo. Never file contents.</summary>
public static class ItemPrompt
{
    public const string Skill = "skill";
    public const string Workflow = "workflow";

    public static string SystemFor(string kind) => kind switch
    {
        Skill => "You write Claude Code skill definition files (SKILL.md). Reply with the file content only.",
        Workflow => "You write short, concrete project workflow files for AI coding agents. Reply with the file content only.",
        _ => AgentPrompt.SystemPrompt
    };

    public static string Build(AgentGenerationRequest r) => r.Kind switch
    {
        Skill => BuildSkill(r),
        Workflow => BuildWorkflow(r),
        _ => AgentPrompt.Build(r.Description, r.Stack, r.ExistingAgents)
    };

    private static string List(IReadOnlyList<string>? names) => names is { Count: > 0 } ? string.Join(", ", names) : "none";

    private static string BuildSkill(AgentGenerationRequest r) =>
        $"""
        Create a Claude Code skill (a SKILL.md Markdown file with YAML frontmatter) for a software repository.

        Repository tech stack: {r.Stack}
        Existing skills in this repository (do not duplicate them): {List(r.ExistingSkills)}

        The user describes the skill they want (it may be in Polish):
        <description>
        {r.Description}
        </description>

        Rules:
        - Reply with the file content only: no explanations and no code fence around the whole file.
        - The file starts with a frontmatter block containing: name, description.
        - name: lowercase English kebab-case, 2-40 characters, different from the existing skills.
        - description: 1-2 sentences, under 300 characters, that say what the skill does and when Claude should use it; put the main use case first. Same language as the user's description.
        - After the frontmatter: short imperative instructions Claude follows once the skill is invoked (4-10 steps or rules), in the same language as the user's description. Name concrete commands or files only when the user gave them. Keep the whole file under 60 lines.
        """;

    private static string BuildWorkflow(AgentGenerationRequest r) =>
        $"""
        Create a project workflow file (a Markdown file with YAML frontmatter): a repeatable procedure that AI coding agents follow in one software repository, such as how to add an API endpoint, check the UI or prepare a commit.

        Repository tech stack: {r.Stack}
        Existing workflows in this repository (do not duplicate them): {List(r.ExistingWorkflows)}
        Agents of this repository (the workflow may use them): {List(r.ExistingAgents)}
        Skills of this repository (the workflow may use them): {List(r.ExistingSkills)}

        The user describes the workflow they want (it may be in Polish):
        <description>
        {r.Description}
        </description>

        Rules:
        - Reply with the file content only: no explanations and no code fence around the whole file.
        - The file starts with a frontmatter block containing: name, description, when, and optionally agents and skills.
        - name: lowercase English kebab-case, 2-40 characters, different from the existing workflows.
        - description: one sentence saying what the procedure achieves, in the same language as the user's description.
        - when: one short line saying when to follow it (a trigger such as "before every commit"), same language.
        - agents / skills: comma-separated names taken ONLY from the lists above, and only those the procedure really uses; leave the field out when none applies. Never invent names.
        - After the frontmatter: numbered steps (4-10), each one concrete and checkable, in the same language as the user's description. Keep the whole file under 60 lines.
        """;
}
