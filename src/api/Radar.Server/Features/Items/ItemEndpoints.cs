using Radar.Scanner;
using Radar.Server.Features.Agents;
using Radar.Server.Features.Files;
using Radar.Server.Features.Gaps;
using Radar.Server.Features.Scans;
using Radar.Server.Features.Skills;
using Radar.Server.Features.Visibility;
using Radar.Server.Infrastructure.Localization;

namespace Radar.Server.Features.Items;

/// <param name="Kind">skill | workflow (agents have their own endpoints)</param>
public sealed record GenerateItemRequest(string? Kind, string? RepoId, string? Description);

/// <param name="Visibility">private also puts the new file or folder on the repo's private list (.git/info/exclude); omitted or public leaves it as git sees it</param>
public sealed record CreateItemRequest(string? Kind, string? RepoId, string? Content, string? Visibility = null);

public static class ItemEndpoints
{
    private static bool KnownKind(string? kind) => kind is ItemPrompt.Skill or ItemPrompt.Workflow;

    private static bool Taken(ScanResult result, RepoInfo repo, string kind, string name) => kind == ItemPrompt.Skill
        ? repo.Skills.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) || Directory.Exists(Path.Combine(GapCommands.DirOf(result, repo), ".claude", "skills", name))
        : result.Workflows.Any(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && w.Repos.Any(r => r.RepoId == repo.Id))
          || File.Exists(Path.Combine(GapCommands.DirOf(result, repo), ".claude", "workflows", name + ".md"));

    private static string RelativePath(string kind, string name) => kind == ItemPrompt.Skill ? $".claude/skills/{name}/SKILL.md" : $".claude/workflows/{name}.md";

    // New skill or workflow from a natural-language description, the same two steps as for agents:
    // 1) generate: one tool-less claude call returns a draft, nothing is written.
    // 2) create:   after the user confirms the app writes the new file (a skill gets its own folder), never overwriting.
    public static void MapItems(this RouteGroupBuilder api)
    {
        api.MapPost("/items/generate", async (GenerateItemRequest req, LatestScanCache cache, IAgentGenerator generator, GenerationGate gate, RequestMessages m, CancellationToken ct) =>
        {
            if (!KnownKind(req.Kind)) return Results.BadRequest(new { error = m[Msg.ItemKindInvalid] });
            var description = req.Description?.Trim();
            if (string.IsNullOrEmpty(description)) return Results.BadRequest(new { error = m[Msg.ItemDescriptionRequired] });
            if (description.Length > AgentPrompt.MaxDescription) return Results.BadRequest(new { error = m.T(Msg.DescriptionTooLong, AgentPrompt.MaxDescription) });

            var result = await cache.GetAsync(ct);
            if (result is null) return Results.NotFound(new { error = m[Msg.NoScan] });
            var repo = result.Repos.FirstOrDefault(r => r.Id == req.RepoId);
            if (repo is null) return Results.NotFound(new { error = m[Msg.UnknownRepo] });

            if (!gate.TryEnter()) return Results.Json(new { error = m[Msg.GenerationBusy] }, statusCode: StatusCodes.Status409Conflict);
            try
            {
                var workflows = result.Workflows.Where(w => w.Repos.Any(r => r.RepoId == repo.Id)).Select(w => w.Name).ToList();
                var generated = await generator.GenerateAsync(new AgentGenerationRequest(
                    description, repo.Stack, repo.Agents.Select(a => a.Name).ToList(), req.Kind!, repo.Skills.Select(s => s.Name).ToList(), workflows), ct);
                var content = AgentValidator.Normalize(generated.Text);
                var validation = AgentValidator.Validate(content, m.Lang);
                var errors = validation.Errors.ToList();
                if (validation.Name is not null && Taken(result, repo, req.Kind!, validation.Name)) errors.Add(m[Msg.ItemNameTaken]);
                return Results.Ok(new
                {
                    name = validation.Name ?? string.Empty,
                    path = validation.Name is null ? null : RelativePath(req.Kind!, validation.Name),
                    content,
                    valid = errors.Count == 0,
                    errors,
                    costUsd = generated.CostUsd
                });
            }
            catch (GeneratorException e)
            {
                return Results.Json(new { error = e.Key is { } key ? m.T(key, e.Args) : e.Message }, statusCode: e.Status);
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(499); // the client stopped waiting
            }
            finally
            {
                gate.Leave();
            }
        });

        api.MapPost("/items", async (CreateItemRequest req, LatestScanCache cache, RequestMessages m, CancellationToken ct) =>
        {
            if (!KnownKind(req.Kind)) return Results.BadRequest(new { error = m[Msg.ItemKindInvalid] });
            if (string.IsNullOrWhiteSpace(req.Content)) return Results.BadRequest(new { error = m[Msg.ContentRequired] });

            var result = await cache.GetAsync(ct);
            if (result is null) return Results.NotFound(new { error = m[Msg.NoScan] });
            var repo = result.Repos.FirstOrDefault(r => r.Id == req.RepoId);
            if (repo is null) return Results.NotFound(new { error = m[Msg.UnknownRepo] });

            var validation = AgentValidator.Validate(req.Content, m.Lang);
            if (!validation.Valid || validation.Name is null) return Results.BadRequest(new { error = m[Msg.AgentInvalid], errors = validation.Errors });
            var name = validation.Name;
            if (Taken(result, repo, req.Kind!, name)) return Results.Json(new { error = m[Msg.ItemExists] }, statusCode: StatusCodes.Status409Conflict);

            var isSkill = req.Kind == ItemPrompt.Skill;
            var hide = req.Visibility == Visibilities.Private;
            var dir = GapCommands.DirOf(result, repo);
            var hidePath = isSkill ? $".claude/skills/{name}" : RelativePath(req.Kind!, name);
            // private: the new file has to be hideable before it exists, so git never offers it for commit
            if (hide && Directory.Exists(dir) && ItemVisibility.Hide(dir, hidePath, isSkill, apply: false) is { } why)
                return Results.Json(new { error = m[Msg.VisibilityCannotHide], blocked = why }, statusCode: StatusCodes.Status409Conflict);

            var text = req.Content.Replace("\r\n", "\n").TrimEnd() + "\n";
            var written = isSkill
                ? SkillWriter.WriteNew(result, repo, new SkillBundle(name, name, [new SkillFile("SKILL.md", new System.Text.UTF8Encoding(false).GetBytes(text), false)], []))
                : AgentWriter.WriteNew(result, repo, name, text, "workflows");

            if (hide && written.Status == WriteStatus.Created)
            {
                try { if (ItemVisibility.Hide(dir, hidePath, isSkill, apply: true) is not null) throw new VisibilityFailure(Msg.VisibilityCannotHide); }
                catch (VisibilityFailure e)
                {
                    if (isSkill) SkillWriter.TryRemove(dir, name);
                    else try { File.Delete(Path.Combine(dir, ".claude", "workflows", name + ".md")); } catch (IOException) { /* we created it a moment ago */ }
                    return Results.Json(new { error = m.T(e.Key, e.Args) }, statusCode: StatusCodes.Status409Conflict);
                }
            }
            return written.Status switch
            {
                WriteStatus.Created => Results.Created($"/api/file?repo={Uri.EscapeDataString(repo.Id)}&path={Uri.EscapeDataString(written.RelativePath!)}", new { path = written.RelativePath, hidden = hide }),
                WriteStatus.Exists => Results.Json(new { error = m[Msg.ItemExists] }, statusCode: StatusCodes.Status409Conflict),
                WriteStatus.RepoMissing => Results.NotFound(new { error = m[Msg.RepoDirMissing] }),
                _ => Results.Json(new { error = m[Msg.WriteForbidden] }, statusCode: StatusCodes.Status403Forbidden)
            };
        });
    }
}
