using Radar.Server.Infrastructure.Storage;
namespace Radar.Server.Features.Gaps;

public interface IToolLocator
{
    /// <summary>Full path of the executable, or null when it is not on PATH.</summary>
    string? Find(string tool);

    bool IsAvailable(string tool) => Find(tool) is not null;
}

/// <summary>Looks for `claude` / `codex` on PATH (plus a few usual install folders). Only informational: the terminal has its own PATH.</summary>
public sealed class PathToolLocator : IToolLocator
{
    private readonly IReadOnlyList<string> _dirs;

    public PathToolLocator() : this(SearchDirs()) { }

    public PathToolLocator(IReadOnlyList<string> dirs) => _dirs = dirs;

    public string? Find(string tool)
    {
        foreach (var dir in _dirs)
        {
            foreach (var name in Candidates(tool))
            {
                try
                {
                    var p = Path.Combine(dir, name);
                    if (File.Exists(p) && (OperatingSystem.IsWindows() || IsExecutable(p))) return p;
                }
                catch (ArgumentException) { /* malformed PATH entry */ }
            }
        }
        return null;
    }

    private static IEnumerable<string> Candidates(string tool)
    {
        if (!OperatingSystem.IsWindows()) return [tool];
        var exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries);
        return exts.Select(e => tool + e.ToLowerInvariant());
    }

    private static bool IsExecutable(string path) =>
        (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;

    private static IReadOnlyList<string> SearchDirs() =>
        SearchDirs(AppPaths.Home, Environment.GetEnvironmentVariable("NVM_DIR"), Environment.GetEnvironmentVariable("PATH"));

    /// <summary>
    /// PATH first, then the usual install folders. Global npm packages live in the bin folder of the Node version that installed them, and a new shell
    /// only has the nvm default on its PATH, so every nvm version is searched too (newest first): `claude` installed under another version is still found.
    /// </summary>
    public static IReadOnlyList<string> SearchDirs(string home, string? nvmDir, string? path)
    {
        var dirs = (path ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        dirs.AddRange([
            Path.Combine(home, ".local", "bin"), Path.Combine(home, ".claude", "local"),
            "/opt/homebrew/bin", "/usr/local/bin",
            Path.Combine(home, ".npm-global", "bin"), Path.Combine(home, ".volta", "bin"),
            Path.Combine(home, "AppData", "Roaming", "npm"), Path.Combine(home, "AppData", "Local", "Programs", "claude")
        ]);
        dirs.AddRange(NvmBinDirs(string.IsNullOrWhiteSpace(nvmDir) ? Path.Combine(home, ".nvm") : nvmDir));
        return dirs;
    }

    private static IEnumerable<string> NvmBinDirs(string nvmDir)
    {
        var versions = Path.Combine(nvmDir, "versions", "node");
        try
        {
            if (!Directory.Exists(versions)) return [];
            return Directory.EnumerateDirectories(versions)
                .OrderByDescending(d => VersionKey(Path.GetFileName(d)))
                .ThenByDescending(d => d, StringComparer.Ordinal)
                .Select(d => Path.Combine(d, "bin"))
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>"v20.9.0" as a comparable number (major, minor, patch); anything that does not parse sorts last.</summary>
    private static long VersionKey(string name)
    {
        var parts = name.TrimStart('v').Split('.');
        long key = 0;
        for (var i = 0; i < 3; i++) key = key * 100000 + (i < parts.Length && long.TryParse(parts[i], out var n) && n < 100000 ? n : 0);
        return key;
    }
}
