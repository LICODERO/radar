using System.Text.Json;
using Radar.Scanner;

namespace Radar.Server.Infrastructure.Storage;

public sealed record AppSettings(string? ScanPath, int MaxDepth = 4);

public interface IScanStore
{
    /// <summary>Latest saved scan as raw JSON (streamed as-is to the UI), or null.</summary>
    Task<byte[]?> LoadLatestJsonAsync(CancellationToken ct = default);
    Task SaveAsync(ScanResult result, CancellationToken ct = default);
}

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}

/// <summary>App state as JSON files in the data directory. Written atomically (temp file + move).</summary>
public sealed class JsonFileStore(string dataDir) : IScanStore, ISettingsStore
{
    private readonly object _gate = new();
    private string ScanFile => Path.Combine(dataDir, "scan-result.json");
    private string SettingsFile => Path.Combine(dataDir, "settings.json");

    public async Task<byte[]?> LoadLatestJsonAsync(CancellationToken ct = default)
    {
        try { return File.Exists(ScanFile) ? await File.ReadAllBytesAsync(ScanFile, ct) : null; }
        catch (IOException) { return null; }
    }

    public Task SaveAsync(ScanResult result, CancellationToken ct = default)
    {
        WriteAtomic(ScanFile, JsonSerializer.SerializeToUtf8Bytes(result, RadarJson.Options));
        return Task.CompletedTask;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return new AppSettings(null);
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllBytes(SettingsFile), RadarJson.Options) ?? new AppSettings(null);
        }
        catch (Exception e) when (e is IOException or JsonException) { return new AppSettings(null); }
    }

    public void Save(AppSettings settings) => WriteAtomic(SettingsFile, JsonSerializer.SerializeToUtf8Bytes(settings, RadarJson.Options));

    private void WriteAtomic(string target, byte[] bytes)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(dataDir);
            var tmp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, target, overwrite: true);
        }
    }
}
