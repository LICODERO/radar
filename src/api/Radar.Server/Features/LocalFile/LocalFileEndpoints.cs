using Radar.Server.Features.Scans;
using Radar.Server.Infrastructure.Localization;

namespace Radar.Server.Features.LocalFile;

/// <param name="IncludePublic">also list the repo's public workflows in a marked block of CLAUDE.md (a tracked file, so it needs a commit)</param>
/// <param name="Confirm">without it nothing is written: the answer is the plan</param>
public sealed record LocalFileRequest(string? RepoId, bool IncludePublic, bool Confirm);

public static class LocalFileEndpoints
{
    public static void MapLocalFile(this RouteGroupBuilder api)
    {
        api.MapPost("/local-file", async (LocalFileRequest req, LatestScanCache cache, RequestMessages m, CancellationToken ct) =>
        {
            var result = await cache.GetAsync(ct);
            if (result is null) return Results.NotFound(new { error = m[Msg.NoScan] });
            var repo = result.Repos.FirstOrDefault(r => r.Id == req.RepoId);
            if (repo is null) return Results.NotFound(new { error = m[Msg.UnknownRepo] });

            try
            {
                var plan = LocalFileService.Run(result, repo, req.IncludePublic, req.Confirm);
                return plan is null ? Results.NotFound(new { error = m[Msg.RepoDirMissing] }) : Results.Ok(plan);
            }
            catch (LocalFileFailure)
            {
                return Results.Json(new { error = m[Msg.LocalFileWriteFailed] }, statusCode: StatusCodes.Status500InternalServerError);
            }
        });
    }
}
