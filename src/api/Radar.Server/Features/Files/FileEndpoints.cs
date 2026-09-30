using Radar.Server.Features.Scans;
namespace Radar.Server.Features.Files;

public static class FileEndpoints
{
    // Read-only preview of a file reported by the latest scan.
    public static void MapFiles(this RouteGroupBuilder api)
    {
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
    }
}
