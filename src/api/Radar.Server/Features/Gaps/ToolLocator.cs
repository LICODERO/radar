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

    private static IReadOnlyList<string> SearchDirs()
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        var home = AppPaths.Home;
        dirs.AddRange([
            Path.Combine(home, ".local", "bin"), Path.Combine(home, ".claude", "local"),
            "/opt/homebrew/bin", "/usr/local/bin",
            Path.Combine(home, "AppData", "Roaming", "npm"), Path.Combine(home, "AppData", "Local", "Programs", "claude")
        ]);
        return dirs;
    }
}
