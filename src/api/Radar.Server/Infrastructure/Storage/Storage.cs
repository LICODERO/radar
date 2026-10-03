using System.Text.Json;
using Radar.Scanner;

namespace Radar.Server.Infrastructure.Storage;

/// <param name="VaultPath">root of the second brain vault; null until the user creates or links one</param>
public sealed record AppSettings(string? ScanPath, int MaxDepth = 4, string? VaultPath = null);

public interface IScanStore
{
    /// <summary>Latest saved scan as raw JSON (streamed as-is to the UI), or null.</summary>
    Task<byte[]?> LoadLatestJsonAsync(CancellationToken ct = default);
    /// <summary>Saves the scan. The scan it replaces is kept as the "previous" one when they differ in content, so the changes are not lost to a quick rescan.</summary>
    Task SaveAsync(ScanResult result, CancellationToken ct = default);

    /// <summary>The scan before the latest one that differed from it (same directory only), or null.</summary>
    Task<ScanResult?> LoadPreviousAsync(CancellationToken ct = default);
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
    private string PreviousFile => Path.Combine(dataDir, "scan-previous.json");
    private string SettingsFile => Path.Combine(dataDir, "settings.json");

    public async Task<byte[]?> LoadLatestJsonAsync(CancellationToken ct = default)
    {
        try { return File.Exists(ScanFile) ? await File.ReadAllBytesAsync(ScanFile, ct) : null; }
        catch (IOException) { return null; }
    }

    public Task SaveAsync(ScanResult result, CancellationToken ct = default)
    {
        RotatePrevious(result);
        WriteAtomic(ScanFile, JsonSerializer.SerializeToUtf8Bytes(result, RadarJson.Options));
        return Task.CompletedTask;
    }

    public Task<ScanResult?> LoadPreviousAsync(CancellationToken ct = default) => Task.FromResult(Read(PreviousFile));

    /// <summary>The scan on disk becomes "previous" when the new one differs from it; a different directory drops the old history.</summary>
    private void RotatePrevious(ScanResult incoming)
    {
        var latest = Read(ScanFile);
        if (latest is null) return;
        var diff = ScanDiff.Compute(latest, incoming);
        if (diff is null) { lock (_gate) { try { File.Delete(PreviousFile); } catch (IOException) { } } return; }
        if (diff.IsEmpty) return;
        WriteAtomic(PreviousFile, JsonSerializer.SerializeToUtf8Bytes(latest, RadarJson.Options));
    }

    private static ScanResult? Read(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<ScanResult>(File.ReadAllBytes(file), RadarJson.Options) : null; }
        catch (Exception e) when (e is IOException or JsonException) { return null; }
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
