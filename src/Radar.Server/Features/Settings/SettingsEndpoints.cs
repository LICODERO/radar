namespace Radar.Server;

public sealed record SettingsRequest(string? ScanPath);

public static class SettingsEndpoints
{
    public static void MapSettings(this RouteGroupBuilder api)
    {
        api.MapGet("/settings", (ISettingsStore settings, IFolderPicker picker) => Results.Ok(View(Load(settings), picker)));

        api.MapPut("/settings", (SettingsRequest req, ISettingsStore settings, IFolderPicker picker) =>
        {
            if (string.IsNullOrWhiteSpace(req.ScanPath)) return Results.BadRequest(new { error = "Podaj ścieżkę katalogu." });
            string full;
            try { full = AppPaths.Expand(req.ScanPath); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return Results.BadRequest(new { error = "Nieprawidłowa ścieżka." });
            }
            if (Path.GetPathRoot(full) == full) return Results.BadRequest(new { error = "Nie można skanować katalogu głównego dysku." });
            if (!Directory.Exists(full)) return Results.BadRequest(new { error = "Katalog nie istnieje." });
            var updated = Load(settings) with { ScanPath = full };
            settings.Save(updated);
            return Results.Ok(View(updated, picker));
        });

        api.MapPost("/pick-folder", async (IFolderPicker picker, CancellationToken ct) =>
        {
            if (!picker.Supported) return Results.StatusCode(StatusCodes.Status501NotImplemented);
            var path = await picker.PickAsync(ct);
            return path is null ? Results.NoContent() : Results.Ok(new { path });
        });
    }

    /// <summary>Stored settings, with the default scan path filled in when none was saved yet.</summary>
    public static AppSettings Load(ISettingsStore settings)
    {
        var s = settings.Load();
        if (s.ScanPath is not null) return s;
        var fallback = Path.Combine(AppPaths.Home, "projects"); // default from the requirements
        return Directory.Exists(fallback) ? s with { ScanPath = fallback } : s;
    }

    private static object View(AppSettings s, IFolderPicker picker)
    {
        var path = s.ScanPath;
        return new
        {
            scanPath = path,
            scanPathDisplay = AppPaths.Display(path),
            exists = path is not null && Directory.Exists(path),
            maxDepth = s.MaxDepth,
            canPickFolder = picker.Supported
        };
    }
}
