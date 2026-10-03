using Radar.Scanner;
using Radar.Server.Features.Files;
using Radar.Server.Features.Scans;
using Radar.Server.Infrastructure.Localization;
namespace Radar.Server.Features.Agents;

public sealed record GenerateAgentRequest(string? RepoId, string? Description);

public sealed record CreateAgentRequest(string? RepoId, string? Content);

/// <param name="Confirm">without it nothing is written: the answer only says what would happen in each repo</param>
public sealed record CopyAgentRequest(string? FromRepoId, string? Name, string[]? ToRepoIds, bool Confirm);

public static class AgentEndpoints
{
    // New agent from a natural-language description:
    // 1) generate: one tool-less claude call returns a draft, nothing is written.
    // 2) create:   only after the user confirms (ZAPISZ) the app writes .claude/agents/<name>.md, never overwriting.
    public static void MapAgents(this RouteGroupBuilder api)
    {
        api.MapPost("/agents/generate", async (GenerateAgentRequest req, LatestScanCache cache, IAgentGenerator generator, GenerationGate gate, RequestMessages m, CancellationToken ct) =>
        {
            var description = req.Description?.Trim();
            if (string.IsNullOrEmpty(description)) return Results.BadRequest(new { error = m[Msg.DescriptionRequired] });
            if (description.Length > AgentPrompt.MaxDescription) return Results.BadRequest(new { error = m.T(Msg.DescriptionTooLong, AgentPrompt.MaxDescription) });

            var result = await cache.GetAsync(ct);
            if (result is null) return Results.NotFound(new { error = m[Msg.NoScan] });
            var repo = result.Repos.FirstOrDefault(r => r.Id == req.RepoId);
            if (repo is null) return Results.NotFound(new { error = m[Msg.UnknownRepo] });

            if (!gate.TryEnter()) return Results.Json(new { error = m[Msg.GenerationBusy] }, statusCode: StatusCodes.Status409Conflict);
            try
            {
                var generated = await generator.GenerateAsync(new AgentGenerationRequest(description, repo.Stack, repo.Agents.Select(a => a.Name).ToList()), ct);
                var content = AgentValidator.Normalize(generated.Text);
                var validation = AgentValidator.Validate(content, m.Lang);
                var errors = validation.Errors.ToList();
                var taken = validation.Name is not null && AgentWriter.Exists(result, repo, validation.Name);
                if (taken) errors.Add(m[Msg.AgentNameTakenInRepo]);
                return Results.Ok(new
                {
                    name = validation.Name ?? string.Empty,
                    path = validation.Name is null ? null : $".claude/agents/{validation.Name}.md",
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

        // Copies an agent one repo already has into other repos that lack it: the source file is read through the scan's own
        // validated paths, each target gets a brand-new .claude/agents/<name>.md and nothing is ever overwritten.
        // Without `confirm` the answer is a plan (what would be created, where the name is already taken).
        api.MapPost("/agents/copy", async (CopyAgentRequest req, LatestScanCache cache, RequestMessages m, CancellationToken ct) =>
        {
            var targetIds = (req.ToRepoIds ?? []).Distinct(StringComparer.Ordinal).ToList();
            if (targetIds.Count == 0) return Results.BadRequest(new { error = m[Msg.CopyNoTargets] });
            if (targetIds.Count > AgentCopy.MaxTargets) return Results.BadRequest(new { error = m.T(Msg.CopyTooMany, AgentCopy.MaxTargets) });

            var result = await cache.GetAsync(ct);
            if (result is null) return Results.NotFound(new { error = m[Msg.NoScan] });
            var source = result.Repos.FirstOrDefault(r => r.Id == req.FromRepoId);
            if (source is null) return Results.NotFound(new { error = m[Msg.UnknownRepo] });
            var agent = source.Agents.FirstOrDefault(a => a.Name.Equals(req.Name, StringComparison.OrdinalIgnoreCase));
            if (agent is null) return Results.NotFound(new { error = m[Msg.AgentSourceMissing] });

            var read = FileReader.Read(result, source.Id, agent.Path);
            if (read.Status != FileReadStatus.Ok || read.Content is null || read.Content.Truncated)
                return Results.Json(new { error = m[Msg.CopySourceUnreadable] }, statusCode: StatusCodes.Status422UnprocessableEntity);
            var validation = AgentValidator.Validate(read.Content.Content, m.Lang);
            if (!validation.Valid || validation.Name is null) return Results.BadRequest(new { error = m[Msg.AgentInvalid], errors = validation.Errors });

            var targets = AgentCopy.Run(result, source.Id, validation.Name, read.Content.Content, targetIds, req.Confirm);
            return Results.Ok(new { name = validation.Name, source = agent.Path, written = req.Confirm, targets });
        });

        api.MapPost("/agents", async (CreateAgentRequest req, LatestScanCache cache, RequestMessages m, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Content)) return Results.BadRequest(new { error = m[Msg.ContentRequired] });

            var result = await cache.GetAsync(ct);
            if (result is null) return Results.NotFound(new { error = m[Msg.NoScan] });
            var repo = result.Repos.FirstOrDefault(r => r.Id == req.RepoId);
            if (repo is null) return Results.NotFound(new { error = m[Msg.UnknownRepo] });

            var validation = AgentValidator.Validate(req.Content, m.Lang);
            if (!validation.Valid || validation.Name is null) return Results.BadRequest(new { error = m[Msg.AgentInvalid], errors = validation.Errors });
            if (AgentWriter.Exists(result, repo, validation.Name))
                return Results.Json(new { error = m[Msg.AgentExists] }, statusCode: StatusCodes.Status409Conflict);

            var written = AgentWriter.WriteNew(result, repo, validation.Name, req.Content);
            return written.Status switch
            {
                WriteStatus.Created => Results.Created($"/api/file?repo={Uri.EscapeDataString(repo.Id)}&path={Uri.EscapeDataString(written.RelativePath!)}", new { path = written.RelativePath }),
                WriteStatus.Exists => Results.Json(new { error = m[Msg.AgentExists] }, statusCode: StatusCodes.Status409Conflict),
                WriteStatus.RepoMissing => Results.NotFound(new { error = m[Msg.RepoDirMissing] }),
                _ => Results.Json(new { error = m[Msg.WriteForbidden] }, statusCode: StatusCodes.Status403Forbidden)
            };
        });
    }
}
