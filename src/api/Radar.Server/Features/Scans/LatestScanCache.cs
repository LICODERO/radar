using System.Text.Json;
using Radar.Scanner;
using Radar.Server.Infrastructure.Storage;

namespace Radar.Server.Features.Scans;

/// <summary>In-memory copy of the latest saved scan; used to validate which files may be read.</summary>
public sealed class LatestScanCache(IScanStore store)
{
    private readonly object _lock = new();
    private ScanResult? _cached;

    public void Set(ScanResult result)
    {
        lock (_lock) _cached = result;
    }

    public async Task<ScanResult?> GetAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_cached is not null) return _cached;
        }

        var json = await store.LoadLatestJsonAsync(ct);
        if (json is null) return null;
        ScanResult? loaded;
        try { loaded = JsonSerializer.Deserialize<ScanResult>(json, RadarJson.Options); }
        catch (JsonException) { return null; }

        lock (_lock) _cached ??= loaded;
        return loaded;
    }
}
