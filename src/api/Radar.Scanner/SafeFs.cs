using System.Text;

namespace Radar.Scanner;

/// <summary>Read-only file helpers that refuse to leave the repository through symlinks.</summary>
public static class SafeFs
{
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public const int MaxFileBytes = 256 * 1024;

    /// <summary>True when <paramref name="path"/> (under <paramref name="root"/>) does not pass through a symlink pointing outside the root.</summary>
    public static bool IsInside(string root, string path)
    {
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, PathComparison) && !string.Equals(full, rootFull, PathComparison)) return false;

        var current = rootFull;
        var relative = full.Length > rootFull.Length ? full[(rootFull.Length + 1)..] : string.Empty;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is null) continue;
            try
            {
                var target = info.ResolveLinkTarget(true);
                if (target is null) return false;
                var t = Path.GetFullPath(target.FullName);
                if (!t.StartsWith(rootFull + Path.DirectorySeparatorChar, PathComparison)) return false;
            }
            catch (IOException) { return false; }
        }
        return true;
    }

    /// <summary>Reads at most <see cref="MaxFileBytes"/> of a UTF-8 text file; null when missing, outside the root or unreadable.</summary>
    public static string? ReadText(string root, string path)
    {
        try
        {
            if (!File.Exists(path) || !IsInside(root, path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buf = new byte[(int)Math.Min(fs.Length, MaxFileBytes)];
            var read = 0;
            while (read < buf.Length)
            {
                var n = fs.Read(buf, read, buf.Length - read);
                if (n == 0) break;
                read += n;
            }
            return Encoding.UTF8.GetString(buf, 0, read).TrimStart('﻿');
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return null; }
    }

    /// <summary>Last write time of a file inside the root; null when it is outside, missing or unreadable.</summary>
    public static DateTime? LastWriteUtc(string root, string path)
    {
        try { return File.Exists(path) && IsInside(root, path) ? File.GetLastWriteTimeUtc(path) : null; }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return null; }
    }

    public static IEnumerable<string> EnumerateMarkdown(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return [];
            return Directory.EnumerateFiles(dir, "*.md").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return []; }
    }
}
