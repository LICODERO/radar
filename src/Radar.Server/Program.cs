using System.Diagnostics;
using System.Text.Json;
using Radar.Scanner;
using Radar.Server;

var builder = WebApplication.CreateBuilder(args);

// Local-only server: never listen on anything but the loopback interface.
builder.WebHost.UseUrls(builder.Configuration["Radar:Url"] ?? "http://127.0.0.1:5178");
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

// Resolved lazily so test/host configuration added after this point is honoured.
builder.Services.AddSingleton(sp => new JsonFileStore(AppPaths.DataDir(sp.GetRequiredService<IConfiguration>())));
builder.Services.AddSingleton<IScanStore>(sp => sp.GetRequiredService<JsonFileStore>());
builder.Services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<JsonFileStore>());
builder.Services.AddSingleton<LatestScanCache>();
builder.Services.AddSingleton<SessionToken>();
builder.Services.AddSingleton<ScanManager>();
builder.Services.AddSingleton<IFolderPicker>(FolderPickerFactory.ForCurrentOs());
builder.Services.AddSingleton<ITerminalLauncher>(TerminalLauncherFactory.ForCurrentOs());
builder.Services.AddSingleton<IToolLocator, PathToolLocator>();

var app = builder.Build();

app.UseMiddleware<ApiGuard>();

var api = app.MapGroup("/api");

api.MapGet("/health", () => Results.Ok(new { status = "ok", app = "R.A.D.A.R" }));
api.MapGet("/session", (SessionToken t) => Results.Ok(new { token = t.Value }));

// ---- settings -------------------------------------------------------------------------------
object SettingsView(AppSettings s)
{
    var path = s.ScanPath;
    return new
    {
        scanPath = path,
        scanPathDisplay = AppPaths.Display(path),
        exists = path is not null && Directory.Exists(path),
        maxDepth = s.MaxDepth,
        canPickFolder = app.Services.GetRequiredService<IFolderPicker>().Supported
    };
}

AppSettings LoadSettings(ISettingsStore settings)
{
    var s = settings.Load();
    if (s.ScanPath is not null) return s;
    var fallback = Path.Combine(AppPaths.Home, "projects"); // default from the requirements
    return Directory.Exists(fallback) ? s with { ScanPath = fallback } : s;
}

api.MapGet("/settings", (ISettingsStore settings) => Results.Ok(SettingsView(LoadSettings(settings))));

api.MapPut("/settings", (SettingsRequest req, ISettingsStore settings) =>
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
    var updated = LoadSettings(settings) with { ScanPath = full };
    settings.Save(updated);
    return Results.Ok(SettingsView(updated));
});

api.MapPost("/pick-folder", async (IFolderPicker picker, CancellationToken ct) =>
{
    if (!picker.Supported) return Results.StatusCode(StatusCodes.Status501NotImplemented);
    var path = await picker.PickAsync(ct);
    return path is null ? Results.NoContent() : Results.Ok(new { path });
});

// ---- scans ----------------------------------------------------------------------------------
api.MapGet("/scan/latest", async (IScanStore scans, CancellationToken ct) =>
{
    var json = await scans.LoadLatestJsonAsync(ct);
    return json is null ? Results.NotFound(new { error = "Brak zapisanego skanu." }) : Results.Bytes(json, "application/json");
});

// Read-only preview of a file reported by the latest scan.
api.MapGet("/file", async (string repo, string path, LatestScanCache cache, CancellationToken ct) =>
{
    var r = FileReader.Read(await cache.GetAsync(ct), repo, path);
    return r.Status switch
    {
        FileReadStatus.Ok => Results.Ok(new { path = r.Content!.Path, content = r.Content.Content, truncated = r.Content.Truncated, bytes = r.Content.Bytes }),
        FileReadStatus.NoScan => Results.NotFound(new { error = "Brak zapisanego skanu." }),
        FileReadStatus.UnknownRepo => Results.NotFound(new { error = "Nieznane repozytorium." }),
        FileReadStatus.NotInScan => Results.NotFound(new { error = "Plik nie należy do wyniku skanu." }),
        FileReadStatus.Missing => Results.NotFound(new { error = "Plik już nie istnieje. Uruchom skan ponownie." }),
        _ => Results.Json(new { error = "Odczyt tego pliku jest zabroniony." }, statusCode: StatusCodes.Status403Forbidden)
    };
});

// ---- commands for gaps ----------------------------------------------------------------------
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

api.MapPost("/scans", (ScanManager manager, ISettingsStore settings) =>
{
    var s = LoadSettings(settings);
    if (s.ScanPath is null || !Directory.Exists(s.ScanPath)) return Results.BadRequest(new { error = "Wybierz istniejący katalog skanu." });
    var (session, started) = manager.Start(s.ScanPath, s.MaxDepth);
    return started
        ? Results.Accepted($"/api/scans/{session.Id}/events", new { id = session.Id })
        : Results.Json(new { id = session.Id, error = "Skan już trwa." }, statusCode: StatusCodes.Status409Conflict);
});

api.MapGet("/scans/current", (ScanManager manager) =>
    manager.Running is { } r ? Results.Ok(new { id = r.Id }) : Results.NoContent());

api.MapDelete("/scans/{id}", (string id, ScanManager manager) => manager.Cancel(id) ? Results.NoContent() : Results.NotFound());

api.MapGet("/scans/{id}/events", async (string id, ScanManager manager, HttpContext ctx) =>
{
    var session = manager.Get(id);
    if (session is null) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }

    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-store";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";
    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

    try
    {
        await foreach (var ev in session.Subscribe(ctx.RequestAborted))
        {
            var (name, payload) = SseFormat.Describe(ev);
            await ctx.Response.WriteAsync($"event: {name}\ndata: {payload}\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        }
    }
    catch (OperationCanceledException) { /* client went away */ }
});

// ---- static UI ------------------------------------------------------------------------------
// The Angular build is served as static files (copied to wwwroot by run.sh).
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

if (app.Configuration.GetValue("Radar:OpenBrowser", true))
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var url = app.Urls.FirstOrDefault() ?? "http://127.0.0.1:5178";
        try
        {
            if (OperatingSystem.IsMacOS()) Process.Start("open", url);
            else if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else Process.Start("xdg-open", url);
        }
        catch { /* the URL is printed in the log anyway */ }
    });
}

app.Run();

public sealed record SettingsRequest(string? ScanPath);

public sealed record RunRequest(string? RepoId, string? Type, string? Tool);

public static class SseFormat
{
    public static (string Name, string Json) Describe(ScanEvent ev)
    {
        var name = ev switch
        {
            ScanStarted => "started",
            ScanPhase => "phase",
            RepoFound => "repo-found",
            RepoScanned => "repo-scanned",
            ScanCompleted => "completed",
            ScanCancelled => "cancelled",
            ScanFailed => "error",
            _ => "message"
        };
        return (name, JsonSerializer.Serialize(ev, ev.GetType(), RadarJson.Options));
    }
}

// Exposed for integration tests.
public partial class Program;
