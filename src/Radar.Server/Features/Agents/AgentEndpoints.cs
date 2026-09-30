namespace Radar.Server;

public sealed record GenerateAgentRequest(string? RepoId, string? Description);

public sealed record CreateAgentRequest(string? RepoId, string? Content);

public static class AgentEndpoints
{
    // New agent from a natural-language description:
    // 1) generate: one tool-less claude call returns a draft, nothing is written.
    // 2) create:   only after the user confirms (ZAPISZ) the app writes .claude/agents/<name>.md, never overwriting.
    public static void MapAgents(this RouteGroupBuilder api)
    {
        api.MapPost("/agents/generate", async (GenerateAgentRequest req, LatestScanCache cache, IAgentGenerator generator, GenerationGate gate, CancellationToken ct) =>
        {
            var description = req.Description?.Trim();
            if (string.IsNullOrEmpty(description)) return Results.BadRequest(new { error = "Opisz agenta własnymi słowami." });
            if (description.Length > AgentPrompt.MaxDescription) return Results.BadRequest(new { error = $"Opis jest za długi (max {AgentPrompt.MaxDescription} znaków)." });

            var result = await cache.GetAsync(ct);
            if (result is null) return Results.NotFound(new { error = "Brak zapisanego skanu." });
            var repo = result.Repos.FirstOrDefault(r => r.Id == req.RepoId);
            if (repo is null) return Results.NotFound(new { error = "Nieznane repozytorium." });

            if (!gate.TryEnter()) return Results.Json(new { error = "Trwa już inne generowanie." }, statusCode: StatusCodes.Status409Conflict);
            try
            {
                var generated = await generator.GenerateAsync(new AgentGenerationRequest(description, repo.Stack, repo.Agents.Select(a => a.Name).ToList()), ct);
                var content = AgentValidator.Normalize(generated.Text);
                var validation = AgentValidator.Validate(content);
                var errors = validation.Errors.ToList();
                var taken = validation.Name is not null && AgentWriter.Exists(result, repo, validation.Name);
                if (taken) errors.Add("Agent o tej nazwie już istnieje w tym repozytorium. Zmień nazwę.");
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
                return Results.Json(new { error = e.Message }, statusCode: e.Status);
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

        api.MapPost("/agents", async (CreateAgentRequest req, LatestScanCache cache, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Content)) return Results.BadRequest(new { error = "Brak treści pliku." });

            var result = await cache.GetAsync(ct);
            if (result is null) return Results.NotFound(new { error = "Brak zapisanego skanu." });
            var repo = result.Repos.FirstOrDefault(r => r.Id == req.RepoId);
            if (repo is null) return Results.NotFound(new { error = "Nieznane repozytorium." });

            var validation = AgentValidator.Validate(req.Content);
            if (!validation.Valid || validation.Name is null) return Results.BadRequest(new { error = "Plik agenta jest niepoprawny.", errors = validation.Errors });
            if (AgentWriter.Exists(result, repo, validation.Name))
                return Results.Json(new { error = "Agent o tej nazwie już istnieje. Zmień nazwę." }, statusCode: StatusCodes.Status409Conflict);

            var written = AgentWriter.WriteNew(result, repo, validation.Name, req.Content);
            return written.Status switch
            {
                WriteStatus.Created => Results.Created($"/api/file?repo={Uri.EscapeDataString(repo.Id)}&path={Uri.EscapeDataString(written.RelativePath!)}", new { path = written.RelativePath }),
                WriteStatus.Exists => Results.Json(new { error = "Agent o tej nazwie już istnieje. Zmień nazwę." }, statusCode: StatusCodes.Status409Conflict),
                WriteStatus.RepoMissing => Results.NotFound(new { error = "Katalog repozytorium już nie istnieje. Uruchom skan ponownie." }),
                _ => Results.Json(new { error = "Zapis w tym katalogu jest zabroniony (dowiązanie poza repozytorium)." }, statusCode: StatusCodes.Status403Forbidden)
            };
        });
    }
}
