using Radar.Server.Features.Scans;
using Radar.Server.Infrastructure.Localization;
using Radar.Server.Infrastructure.Storage;

namespace Radar.Server.Features.Vault;

public sealed record VaultPathRequest(string? Path);

public sealed record InstallSkillRequest(bool Update = false);

public sealed record EnableProjectRequest(string? RepoId, bool Confirm = false);

public static class VaultEndpoints
{
    // The second brain: a vault folder outside the repos. Everything the app writes here is built on the server from embedded
    // templates; the client only chooses a folder, a repo and whether to confirm.
    public static void MapVault(this RouteGroupBuilder api)
    {
        api.MapGet("/vault", (VaultService vault) => Results.Ok(View(vault.GetStatus())));

        // create a new vault in an empty or new folder
        api.MapPost("/vault", (VaultPathRequest req, VaultService vault, RequestMessages m) =>
        {
            if (string.IsNullOrWhiteSpace(req.Path)) return Results.BadRequest(new { error = m[Msg.PathRequired] });
            var r = vault.Create(req.Path);
            return r.Outcome == VaultOutcome.Ok ? Results.Created("/api/vault", View(r.Status!)) : Failure(r.Outcome, m);
        });

        // point the app at an existing vault (moved folder, second computer)
        api.MapPut("/vault", (VaultPathRequest req, VaultService vault, RequestMessages m) =>
        {
            if (string.IsNullOrWhiteSpace(req.Path)) return Results.BadRequest(new { error = m[Msg.PathRequired] });
            var r = vault.Relink(req.Path);
            return r.Outcome == VaultOutcome.Ok ? Results.Ok(View(r.Status!)) : Failure(r.Outcome, m);
        });

        api.MapGet("/vault/skill", (SecondBrainSkill skill) => Results.Ok(SkillView(skill.Status())));

        api.MapPost("/vault/skill", (InstallSkillRequest? req, SecondBrainSkill skill, RequestMessages m) =>
        {
            var outcome = skill.Install(req?.Update ?? false);
            return outcome switch
            {
                SkillOutcome.Installed or SkillOutcome.Updated or SkillOutcome.AlreadyInstalled => Results.Ok(new { outcome = outcome.ToString(), skill = SkillView(skill.Status()) }),
                SkillOutcome.UpdateNeeded => Conflict(m[Msg.SkillOutdated]),
                SkillOutcome.Modified => Conflict(m[Msg.SkillModified]),
                SkillOutcome.Foreign => Conflict(m[Msg.SkillForeign]),
                SkillOutcome.Linked => Conflict(m[Msg.SkillLinked]),
                _ => Results.Json(new { error = m[Msg.SkillWriteFailed] }, statusCode: StatusCodes.Status500InternalServerError)
            };
        });

        // second brain for one repo: without confirm it only returns what would be written
        api.MapPost("/vault/projects", async (EnableProjectRequest req, VaultService vault, LatestScanCache cache, RequestMessages m, CancellationToken ct) =>
        {
            var scan = await cache.GetAsync(ct);
            if (scan is null) return Results.NotFound(new { error = m[Msg.NoScan] });
            var repo = scan.Repos.FirstOrDefault(r => r.Id == req.RepoId);
            if (repo is null) return Results.NotFound(new { error = m[Msg.UnknownRepo] });

            var r = vault.Enable(scan, repo, req.Confirm);
            return r.Outcome switch
            {
                EnableOutcome.Ok => Results.Json(new { applied = r.Applied, plan = PlanView(r.Plan!), vault = View(vault.GetStatus()) },
                    statusCode: r.Applied ? StatusCodes.Status201Created : StatusCodes.Status200OK),
                EnableOutcome.VaultNotReady => Conflict(m[Msg.VaultNotReady]),
                EnableOutcome.RepoMissing => Results.NotFound(new { error = m[Msg.RepoDirMissing] }),
                EnableOutcome.RepoOutsideRoot => Results.Json(new { error = m[Msg.RepoOutsideRoot] }, statusCode: StatusCodes.Status403Forbidden),
                EnableOutcome.NameTaken => Conflict(m[Msg.ProjectNameTaken]),
                EnableOutcome.Forbidden => Results.Json(new { error = m[Msg.ClaudeLocalForbidden] }, statusCode: StatusCodes.Status403Forbidden),
                EnableOutcome.BlockDamaged => Conflict(m[Msg.ClaudeLocalDamaged]),
                _ => Results.Json(new { error = m[Msg.VaultWriteFailed] }, statusCode: StatusCodes.Status500InternalServerError)
            };
        });
    }

    private static IResult Conflict(string error) => Results.Json(new { error }, statusCode: StatusCodes.Status409Conflict);

    private static IResult Failure(VaultOutcome outcome, RequestMessages m) => outcome switch
    {
        VaultOutcome.PathInvalid => Results.BadRequest(new { error = m[Msg.PathInvalid] }),
        VaultOutcome.PathIsRoot => Results.BadRequest(new { error = m[Msg.PathIsRoot] }),
        VaultOutcome.FolderMissing => Results.BadRequest(new { error = m[Msg.PathMissing] }),
        VaultOutcome.InsideRepo => Results.BadRequest(new { error = m[Msg.VaultInsideRepo] }),
        VaultOutcome.NotEmpty => Conflict(m[Msg.VaultNotEmpty]),
        VaultOutcome.AlreadyVault => Conflict(m[Msg.VaultAlready]),
        VaultOutcome.NotAVault => Results.BadRequest(new { error = m[Msg.VaultNotAVault] }),
        VaultOutcome.UnsupportedVersion => Results.BadRequest(new { error = m[Msg.VaultTooNew] }),
        _ => Results.Json(new { error = m[Msg.VaultWriteFailed] }, statusCode: StatusCodes.Status500InternalServerError)
    };

    private static object View(VaultStatus s)
    {
        var suggested = Path.Combine(AppPaths.Home, "Documents", "RADAR Second Brain");
        return new
        {
            state = s.State,
            path = s.Path,
            pathDisplay = AppPaths.Display(s.Path),
            reason = s.Reason,
            projects = s.Projects.Select(p => new { name = p.Name, repoId = p.RepoId }),
            suggestedPath = suggested,
            suggestedPathDisplay = AppPaths.Display(suggested)
        };
    }

    private static object SkillView(SkillStatus s) => new
    {
        name = SecondBrainSkill.Name,
        state = ToKebab(s.State.ToString()),
        path = s.Path,
        pathDisplay = AppPaths.Display(s.Path),
        availableVersion = s.AvailableVersion,
        installedVersion = s.InstalledVersion
    };

    private static object PlanView(ProjectPlan p) => new
    {
        vaultDir = p.VaultDir,
        creates = p.Creates,
        claudeLocalPath = p.ClaudeLocalPath,
        claudeLocalExists = p.ClaudeLocalExists,
        block = p.Block,
        alreadyEnabled = p.AlreadyEnabled,
        gitIgnored = p.GitIgnored
    };

    private static string ToKebab(string pascal) =>
        string.Concat(pascal.Select((c, i) => char.IsUpper(c) ? (i == 0 ? "" : "-") + char.ToLowerInvariant(c) : c.ToString()));
}
