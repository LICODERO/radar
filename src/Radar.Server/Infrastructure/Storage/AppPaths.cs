namespace Radar.Server;

public static class AppPaths
{
    /// <summary>Directory for the app's own state (settings, cached scan). Never inside a scanned repo.</summary>
    public static string DataDir(IConfiguration config)
    {
        var overrideDir = config["Radar:DataDir"];
        if (!string.IsNullOrWhiteSpace(overrideDir)) return Path.GetFullPath(overrideDir);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS()) return Path.Combine(home, "Library", "Application Support", "RADAR");
        if (OperatingSystem.IsWindows()) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RADAR");
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return Path.Combine(string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".local", "share") : xdg, "radar");
    }

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Expands a leading ~ and returns a full path.</summary>
    public static string Expand(string path)
    {
        var p = path.Trim();
        if (p == "~") p = Home;
        else if (p.StartsWith("~/", StringComparison.Ordinal) || p.StartsWith("~\\", StringComparison.Ordinal)) p = Path.Combine(Home, p[2..]);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
    }

    /// <summary>Shortens the home directory to ~ for display.</summary>
    public static string Display(string? path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        var home = Path.TrimEndingDirectorySeparator(Home);
        if (path == home) return "~";
        return path.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "~" + path[home.Length..] : path;
    }
}
