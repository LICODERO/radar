using Radar.Scanner;
using Radar.Server.Features.Scans;
using Radar.Server.Infrastructure.Localization;

namespace Radar.Server.Features.Skills;

/// <param name="Confirm">without it nothing is written: the answer only says what would happen in each repo</param>
/// <param name="Visibility">private hides every new copy through .git/info/exclude; omitted or public leaves it as git sees it</param>
public sealed record CopySkillRequest(string? FromRepoId, string? Name, string[]? ToRepoIds, bool Confirm, string? Visibility = null);

public static class SkillEndpoints
{
    // Copies a whole skill folder one repo has into other repos that lack it. The source is read through the scan's own paths,
    // each target gets a brand-new .claude/skills/<name>/ and nothing is ever overwritten. Without `confirm` the answer is a plan.
    public static void MapSkills(this RouteGroupBuilder api)
    {
        api.MapPost("/skills/copy", async (CopySkillRequest req, LatestScanCache cache, RequestMessages m, CancellationToken ct) =>
        {
            var targetIds = (req.ToRepoIds ?? []).Distinct(StringComparer.Ordinal).ToList();
            if (targetIds.Count == 0) return Results.BadRequest(new { error = m[Msg.CopyNoTargets] });
            if (targetIds.Count > SkillCopy.MaxTargets) return Results.BadRequest(new { error = m.T(Msg.CopyTooMany, SkillCopy.MaxTargets) });

            var result = await cache.GetAsync(ct);
            if (result is null) return Results.NotFound(new { error = m[Msg.NoScan] });
            var source = result.Repos.FirstOrDefault(r => r.Id == req.FromRepoId);
            if (source is null) return Results.NotFound(new { error = m[Msg.UnknownRepo] });
            var skill = source.Skills.FirstOrDefault(s => s.Name.Equals(req.Name, StringComparison.OrdinalIgnoreCase));
            if (skill is null) return Results.NotFound(new { error = m[Msg.SkillSourceMissing] });

            var read = SkillBundleReader.Read(result, source, skill);
            if (read.Bundle is null)
            {
                return read.Error switch
                {
                    BundleError.TooManyFiles => Results.Json(new { error = m.T(Msg.SkillTooManyFiles, SkillBundleReader.MaxFiles) }, statusCode: StatusCodes.Status422UnprocessableEntity),
                    BundleError.TooBig => Results.Json(new { error = m.T(Msg.SkillTooBigToCopy, SkillBundleReader.MaxTotalBytes / 1024) }, statusCode: StatusCodes.Status422UnprocessableEntity),
                    BundleError.Unreadable => Results.Json(new { error = m[Msg.SkillSourceUnreadable] }, statusCode: StatusCodes.Status422UnprocessableEntity),
                    _ => Results.BadRequest(new { error = m[Msg.SkillSourceInvalid] })
                };
            }

            var bundle = read.Bundle;
            var targets = SkillCopy.Run(result, source.Id, bundle, targetIds, req.Confirm, req.Visibility == Visibilities.Private);
            return Results.Ok(new
            {
                name = bundle.Name,
                source = skill.Path,
                files = bundle.Files.Select(f => f.RelativePath).ToList(),
                bytes = bundle.Bytes,
                skipped = bundle.Skipped,
                written = req.Confirm,
                targets
            });
        });
    }
}
