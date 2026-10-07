using Radar.Server.Features.Scans;
using Radar.Server.Infrastructure.Localization;

namespace Radar.Server.Features.Relations;

/// <param name="Relations">the whole set of rules wanted afterwards; rules that are missing from it are removed from the repos</param>
/// <param name="Confirm">without it nothing is written: the answer is the plan</param>
public sealed record RelationsRequest(IReadOnlyList<Relation>? Relations, bool Confirm);

public static class RelationsEndpoints
{
    public static void MapRelations(this RouteGroupBuilder api)
    {
        api.MapGet("/relations", async (LatestScanCache cache, RequestMessages m, CancellationToken ct) =>
        {
            var result = await cache.GetAsync(ct);
            return result is null ? Results.NotFound(new { error = m[Msg.NoScan] }) : Results.Ok(RelationsService.Load(result));
        });

        api.MapPost("/relations", async (RelationsRequest req, LatestScanCache cache, RequestMessages m, CancellationToken ct) =>
        {
            var result = await cache.GetAsync(ct);
            if (result is null) return Results.NotFound(new { error = m[Msg.NoScan] });

            var input = req.Relations ?? [];
            if (input.Count > Relation.MaxRelations) return Results.BadRequest(new { error = m.T(Msg.RelationInvalid, "count") });
            var clean = new Dictionary<string, Relation>();
            foreach (var r in input)
            {
                string? why = "empty";
                var ok = r is null ? null : Relation.Clean(r, out why);
                if (ok is null) return Results.BadRequest(new { error = m.T(Msg.RelationInvalid, why!) });
                if (!clean.TryAdd(ok.Key, ok)) return Results.BadRequest(new { error = m.T(Msg.RelationInvalid, "duplicate") });
            }

            try
            {
                var plan = RelationsService.Run(result, clean.Values.ToList(), req.Confirm);
                return plan.Blocked is null ? Results.Ok(plan) : Results.Json(new { error = m.T(Msg.RelationsBlocked, plan.BlockedRepo ?? "?"), plan }, statusCode: StatusCodes.Status409Conflict);
            }
            catch (RelationsFailure)
            {
                return Results.Json(new { error = m[Msg.RelationsWriteFailed] }, statusCode: StatusCodes.Status500InternalServerError);
            }
        });
    }
}
