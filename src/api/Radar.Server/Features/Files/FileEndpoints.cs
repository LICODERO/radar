using Radar.Server.Features.Scans;
using Radar.Server.Infrastructure.Localization;
namespace Radar.Server.Features.Files;

public static class FileEndpoints
{
    // Read-only preview of a file reported by the latest scan.
    public static void MapFiles(this RouteGroupBuilder api)
    {
        api.MapGet("/file", async (string repo, string path, LatestScanCache cache, RequestMessages m, CancellationToken ct) =>
        {
            var r = FileReader.Read(await cache.GetAsync(ct), repo, path);
            return r.Status switch
            {
                FileReadStatus.Ok => Results.Ok(new { path = r.Content!.Path, content = r.Content.Content, truncated = r.Content.Truncated, bytes = r.Content.Bytes }),
                FileReadStatus.NoScan => Results.NotFound(new { error = m[Msg.NoScan] }),
                FileReadStatus.UnknownRepo => Results.NotFound(new { error = m[Msg.UnknownRepo] }),
                FileReadStatus.NotInScan => Results.NotFound(new { error = m[Msg.FileNotInScan] }),
                FileReadStatus.Missing => Results.NotFound(new { error = m[Msg.FileMissing] }),
                _ => Results.Json(new { error = m[Msg.FileForbidden] }, statusCode: StatusCodes.Status403Forbidden)
            };
        });
    }
}
