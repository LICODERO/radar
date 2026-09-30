using Radar.Scanner;
using Radar.Server.Features.Scans;
using Radar.Server.Infrastructure.Localization;

namespace Radar.Server.Features.Gaps;

public sealed record RunRequest(string? RepoId, string? Type, string? Tool);

public static class GapEndpoints
{
    public static void MapGaps(this RouteGroupBuilder api)
    {
        api.MapGet("/gaps", async (LatestScanCache cache, RequestMessages m, CancellationToken ct) =>
        {
            var result = await cache.GetAsync(ct);
            return result is null ? Results.NotFound(new { error = m[Msg.NoScan] }) : Results.Ok(GapCommands.Build(result));
        });

        api.MapGet("/tools", (ITerminalLauncher launcher, IToolLocator locator) => Results.Ok(new
        {
            platform = launcher.Platform,
            shell = launcher.Shell,
            canLaunch = launcher.Supported,
            tools = Tools.Known.ToDictionary(t => t, locator.IsAvailable)
        }));

        // Opens a terminal in the repo and starts claude/codex there. The command is built on the server from the scan
        // result; the client can only choose repo, gap type and tool, never supply command text.
        api.MapPost("/run", async (RunRequest req, LatestScanCache cache, ITerminalLauncher launcher, IToolLocator locator, RequestMessages m, CancellationToken ct) =>
        {
            if (!launcher.Supported) return Results.Json(new { error = m[Msg.RunUnsupported] }, statusCode: StatusCodes.Status501NotImplemented);
            if (!Tools.IsKnown(req.Tool)) return Results.BadRequest(new { error = m[Msg.UnknownTool] });
            if (req.Type is null || !GapCommands.Order.Contains(req.Type)) return Results.BadRequest(new { error = m[Msg.UnknownGap] });

            var result = await cache.GetAsync(ct);
            if (result is null) return Results.NotFound(new { error = m[Msg.NoScan] });
            var repo = result.Repos.FirstOrDefault(r => r.Id == req.RepoId);
            if (repo is null) return Results.NotFound(new { error = m[Msg.UnknownRepo] });
            if (!repo.Gaps.Contains(req.Type)) return Results.BadRequest(new { error = m[Msg.GapAbsent] });

            var item = GapCommands.Item(result, repo, req.Type);
            var root = Path.GetFullPath(result.ScanRoot);
            if (!Directory.Exists(item.Dir)) return Results.NotFound(new { error = m[Msg.RepoDirMissing] });
            if (!SafeFs.IsInside(root, item.Dir)) return Results.Json(new { error = m[Msg.RepoOutsideRoot] }, statusCode: StatusCodes.Status403Forbidden);

            try
            {
                await launcher.LaunchAsync(new LaunchRequest(item.Dir, req.Tool!, item.Prompt, $"R.A.D.A.R · {repo.Name} · {req.Type}"), ct);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
            {
                return Results.Json(new { error = m.T(Msg.TerminalFailed, e.Message) }, statusCode: StatusCodes.Status500InternalServerError);
            }
            return Results.Ok(new { launched = true, toolFound = locator.IsAvailable(req.Tool!) });
        });
    }
}
