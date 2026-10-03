using System.Text.Json;
using Radar.Scanner;
using Radar.Server.Features.Settings;
using Radar.Server.Infrastructure.Storage;
using Radar.Server.Infrastructure.Localization;

namespace Radar.Server.Features.Scans;

/// <summary>Which AI tool's configuration the scan looks for; absent means Claude Code.</summary>
public sealed record StartScanRequest(string? Tool);

public static class ScanEndpoints
{
    /// <summary>The tools the scanner can look for. Codex, Cursor and Antigravity are planned: they need their own detectors.</summary>
    public static readonly string[] ScannableTools = ["claude"];

    public static void MapScans(this RouteGroupBuilder api)
    {
        api.MapGet("/scan/latest", async (IScanStore scans, RequestMessages m, CancellationToken ct) =>
        {
            var json = await scans.LoadLatestJsonAsync(ct);
            return json is null ? Results.NotFound(new { error = m[Msg.NoScan] }) : Results.Bytes(json, "application/json");
        });

        // What moved since the last scan that looked different (204 when there is nothing to compare yet).
        api.MapGet("/scan/changes", async (IScanStore scans, LatestScanCache cache, CancellationToken ct) =>
        {
            var current = await cache.GetAsync(ct);
            var previous = await scans.LoadPreviousAsync(ct);
            if (current is null || previous is null) return Results.NoContent();
            return ScanDiff.Compute(previous, current) is { } changes ? Results.Ok(changes) : Results.NoContent();
        });

        api.MapPost("/scans", (StartScanRequest? req, ScanManager manager, ISettingsStore settings, RequestMessages m) =>
        {
            if (req?.Tool is { } tool && !ScannableTools.Contains(tool, StringComparer.Ordinal)) return Results.BadRequest(new { error = m[Msg.ScanToolUnsupported] });
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
