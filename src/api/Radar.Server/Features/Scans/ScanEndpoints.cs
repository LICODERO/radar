using System.Text.Json;
using Radar.Scanner;
using Radar.Server.Features.Settings;
using Radar.Server.Infrastructure.Storage;
using Radar.Server.Infrastructure.Localization;

namespace Radar.Server.Features.Scans;

public static class ScanEndpoints
{
    public static void MapScans(this RouteGroupBuilder api)
    {
        api.MapGet("/scan/latest", async (IScanStore scans, RequestMessages m, CancellationToken ct) =>
        {
            var json = await scans.LoadLatestJsonAsync(ct);
            return json is null ? Results.NotFound(new { error = m[Msg.NoScan] }) : Results.Bytes(json, "application/json");
        });

        api.MapPost("/scans", (ScanManager manager, ISettingsStore settings, RequestMessages m) =>
        {
            var s = SettingsEndpoints.Load(settings);
            if (s.ScanPath is null || !Directory.Exists(s.ScanPath)) return Results.BadRequest(new { error = m[Msg.PickScanDir] });
            var (session, started) = manager.Start(s.ScanPath, s.MaxDepth);
            return started
                ? Results.Accepted($"/api/scans/{session.Id}/events", new { id = session.Id })
                : Results.Json(new { id = session.Id, error = m[Msg.ScanRunning] }, statusCode: StatusCodes.Status409Conflict);
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
    }
}

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
