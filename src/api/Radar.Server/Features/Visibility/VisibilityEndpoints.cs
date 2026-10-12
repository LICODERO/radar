using Radar.Scanner;
using Radar.Server.Features.LocalFile;
using Radar.Server.Features.Scans;
using Radar.Server.Infrastructure.Localization;

namespace Radar.Server.Features.Visibility;

/// <param name="Path">repo-relative path of an agent, a skill's SKILL.md or a workflow, as the scan reported it</param>
/// <param name="Visibility">private | public</param>
/// <param name="Confirm">without it nothing is written: the answer is the plan</param>
public sealed record SetVisibilityRequest(string? RepoId, string? Path, string? Visibility, bool Confirm);

public static class VisibilityEndpoints
{
    public static void MapVisibility(this RouteGroupBuilder api)
    {
        api.MapPost("/visibility", async (SetVisibilityRequest req, LatestScanCache cache, RequestMessages m, CancellationToken ct) =>
        {
            if (req.Visibility is not (Visibilities.Private or Visibilities.Public)) return Results.BadRequest(new { error = m[Msg.VisibilityBadTarget] });

            var result = await cache.GetAsync(ct);
            if (result is null) return Results.NotFound(new { error = m[Msg.NoScan] });
            var repo = result.Repos.FirstOrDefault(r => r.Id == req.RepoId);
            if (repo is null) return Results.NotFound(new { error = m[Msg.UnknownRepo] });

            try
            {
                var outcome = ItemVisibility.Run(result, repo, req.Path ?? string.Empty, req.Visibility, req.Confirm);
                if (outcome is null) return Results.NotFound(new { error = m[Msg.FileNotInScan] });

                // a workflow is only known to Claude through the index in CLAUDE.local.md, so that follows the change
                var isWorkflow = result.Workflows.Any(w => w.Repos.Any(r => r.RepoId == repo.Id && r.Path == req.Path));
                if (isWorkflow && outcome.Blocked is null && outcome.Changes.Count > 0)
                {
                    outcome = outcome with { Notes = [.. outcome.Notes, "local-index"] };
                    if (outcome.Applied)
                        outcome = outcome with { LocalIndex = LocalFileService.Refresh(result, repo, new PendingWorkflow(req.Path!, outcome.Visibility, null)) };
                }
                return Results.Ok(outcome);
            }
            catch (VisibilityFailure e)
            {
                return Results.Json(new { error = m.T(e.Key, e.Args) }, statusCode: StatusCodes.Status500InternalServerError);
            }
        });
    }
}
