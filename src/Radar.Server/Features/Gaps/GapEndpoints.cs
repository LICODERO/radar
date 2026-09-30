using Radar.Scanner;

namespace Radar.Server;

public sealed record RunRequest(string? RepoId, string? Type, string? Tool);

public static class GapEndpoints
{
    public static void MapGaps(this RouteGroupBuilder api)
    {
        api.MapGet("/gaps", async (LatestScanCache cache, CancellationToken ct) =>
        {
            var result = await cache.GetAsync(ct);
            return result is null ? Results.NotFound(new { error = "Brak zapisanego skanu." }) : Results.Ok(GapCommands.Build(result));
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
        api.MapPost("/run", async (RunRequest req, LatestScanCache cache, ITerminalLauncher launcher, IToolLocator locator, CancellationToken ct) =>
        {
            if (!launcher.Supported) return Results.Json(new { error = "Uruchamianie terminala nie jest dostępne na tym systemie. Skopiuj polecenie." }, statusCode: StatusCodes.Status501NotImplemented);
            if (!Tools.IsKnown(req.Tool)) return Results.BadRequest(new { error = "Nieznane narzędzie." });
            if (req.Type is null || !GapCommands.Order.Contains(req.Type)) return Results.BadRequest(new { error = "Nieznany typ luki." });

            var result = await cache.GetAsync(ct);
            if (result is null) return Results.NotFound(new { error = "Brak zapisanego skanu." });
            var repo = result.Repos.FirstOrDefault(r => r.Id == req.RepoId);
            if (repo is null) return Results.NotFound(new { error = "Nieznane repozytorium." });
            if (!repo.Gaps.Contains(req.Type)) return Results.BadRequest(new { error = "Ta luka nie występuje w wyniku skanu." });

            var item = GapCommands.Item(result, repo, req.Type);
            var root = Path.GetFullPath(result.ScanRoot);
            if (!Directory.Exists(item.Dir)) return Results.NotFound(new { error = "Katalog repozytorium już nie istnieje. Uruchom skan ponownie." });
            if (!SafeFs.IsInside(root, item.Dir)) return Results.Json(new { error = "Katalog repozytorium jest poza katalogiem skanu." }, statusCode: StatusCodes.Status403Forbidden);

            try
            {
                await launcher.LaunchAsync(new LaunchRequest(item.Dir, req.Tool!, item.Prompt, $"R.A.D.A.R · {repo.Name} · {req.Type}"), ct);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
            {
                return Results.Json(new { error = $"Nie udało się otworzyć terminala: {e.Message}" }, statusCode: StatusCodes.Status500InternalServerError);
            }
            return Results.Ok(new { launched = true, toolFound = locator.IsAvailable(req.Tool!) });
        });
    }
}
