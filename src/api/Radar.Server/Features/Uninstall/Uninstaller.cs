using System.Diagnostics;
using Radar.Server.Features.Vault;

namespace Radar.Server.Features.Uninstall;

public enum UninstallKind { Link, UserPath, Skill, Data, App }

/// <summary>One thing the uninstall would remove.</summary>
public sealed record UninstallItem(UninstallKind Kind, string Path, string Detail);

/// <summary>What the uninstall would remove (<see cref="Items"/>) and what it leaves on purpose or cannot touch (<see cref="Skipped"/>).</summary>
public sealed record UninstallPlan(IReadOnlyList<UninstallItem> Items, IReadOnlyList<string> Skipped);

/// <summary>Everything the uninstall needs from the machine, so tests can point it at temp folders.</summary>
/// <param name="AppDir">The folder this copy of the program runs from.</param>
/// <param name="BinDir">Where the installer links <c>radar</c> (macOS/Linux).</param>
/// <param name="DataDir">The app's own settings and cached scan.</param>
/// <param name="ReadUserPath">The user PATH variable (Windows only; null elsewhere).</param>
/// <param name="WriteUserPath">Replaces the user PATH variable (Windows only).</param>
/// <param name="DeleteAppDir">Removes the program folder; null deletes it right away. Windows cannot delete a running executable,
/// so the real command hands this to a helper that waits for the process to exit.</param>
public sealed record UninstallEnvironment(
    string AppDir,
    string BinDir,
    string DataDir,
    SecondBrainSkill Skill,
    bool Windows,
    Func<string?>? ReadUserPath = null,
    Action<string>? WriteUserPath = null,
    Action<string>? DeleteAppDir = null);

/// <summary>
/// Removes what the installers put on the machine: the program folder, the <c>radar</c> link or user PATH entry and, only when
/// asked, the app's settings and the bundled skill. Never touches a repository, the second-brain vault or a shell profile.
/// </summary>
public sealed class Uninstaller(UninstallEnvironment env)
{
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private string ExeName => env.Windows ? "radar.exe" : "radar";
    private string Exe => Path.Combine(env.AppDir, ExeName);

    public UninstallPlan Plan(bool includeData, bool includeSkill)
    {
        var items = new List<UninstallItem>();
        var skipped = new List<string>();

        var appOk = IsInstalledCopy(out var whyNot);
        if (!appOk) skipped.Add($"Program files in {env.AppDir}: left alone, {whyNot}");

        if (appOk)
        {
            if (!env.Windows) PlanLink(items, skipped);
            else PlanUserPath(items);
            items.Add(new UninstallItem(UninstallKind.App, env.AppDir, "the program and its UI"));
        }

        if (includeSkill) PlanSkill(items, skipped);

        if (includeData)
        {
            if (Directory.Exists(env.DataDir)) items.Add(new UninstallItem(UninstallKind.Data, env.DataDir, "settings and the last scan"));
            else skipped.Add($"Settings in {env.DataDir}: nothing there");
        }

        // the order of removal: things that point at the program first, the program last
        items.Sort((a, b) => a.Kind.CompareTo(b.Kind));
        return new UninstallPlan(items, skipped);
    }

    /// <summary>Removes every item of the plan; returns the failures (an empty list means everything went well).</summary>
    public List<string> Apply(UninstallPlan plan)
    {
        var failures = new List<string>();
        foreach (var item in plan.Items)
        {
            try { Remove(item); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                failures.Add($"{item.Path}: {e.Message}");
            }
        }
        return failures;
    }

    private void Remove(UninstallItem item)
    {
        switch (item.Kind)
        {
            case UninstallKind.Link:
                File.Delete(item.Path);
                break;
            case UninstallKind.UserPath:
                RemoveUserPathEntry();
                break;
            case UninstallKind.Skill:
                File.Delete(item.Path);
                var dir = Path.GetDirectoryName(item.Path)!;
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                break;
            case UninstallKind.Data:
                Directory.Delete(item.Path, recursive: true);
                break;
            case UninstallKind.App:
                if (env.DeleteAppDir is { } later) later(item.Path);
                else
                {
                    Directory.Delete(item.Path, recursive: true);
                    var root = RootToRemove(item.Path);
                    if (root is not null && !Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
                }
                break;
        }
    }

    /// <summary>The installer puts the program in <c>~/.radar/app</c> (<c>%LOCALAPPDATA%\RADAR\app</c>); the folder around it goes too once empty.</summary>
    public static string? RootToRemove(string appDir)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(appDir));
        var name = parent is null ? null : Path.GetFileName(parent);
        return name is ".radar" or "RADAR" ? parent : null;
    }

    // A published copy has the UI in wwwroot next to the executable. A build output (dotnet run, the tests) has none, and a checkout
    // must never be deleted, so both are refused.
    private bool IsInstalledCopy(out string whyNot)
    {
        if (!File.Exists(Exe) || !Directory.Exists(Path.Combine(env.AppDir, "wwwroot")))
        {
            whyNot = "it does not look like an installed copy (no wwwroot folder next to the executable).";
            return false;
        }
        for (var dir = new DirectoryInfo(env.AppDir); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                whyNot = $"it sits inside a git repository ({dir.FullName}).";
                return false;
            }
        }
        whyNot = "";
        return true;
    }

    private void PlanLink(List<UninstallItem> items, List<string> skipped)
    {
        var link = Path.Combine(env.BinDir, "radar");
        var info = new FileInfo(link);
        if (info.LinkTarget is null)
        {
            if (File.Exists(link)) skipped.Add($"{link}: left alone, it is not a link made by the installer.");
            return;
        }
        string? target = null;
        try { target = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName; }
        catch (IOException) { /* a broken link points nowhere we know */ }
        if (target is not null && string.Equals(target, Path.GetFullPath(Exe), PathComparison))
            items.Add(new UninstallItem(UninstallKind.Link, link, "the radar command"));
        else
            skipped.Add($"{link}: left alone, it points to {info.LinkTarget}, not to this copy.");
    }

    private void PlanUserPath(List<UninstallItem> items)
    {
        if (env.ReadUserPath?.Invoke() is { } path && SplitPath(path).Any(IsAppDir))
            items.Add(new UninstallItem(UninstallKind.UserPath, env.AppDir, "the entry in your user PATH"));
    }

    private void RemoveUserPathEntry()
    {
        var path = env.ReadUserPath?.Invoke();
        if (path is null || env.WriteUserPath is null) return;
        env.WriteUserPath(string.Join(';', SplitPath(path).Where(e => !IsAppDir(e))));
    }

    private static IEnumerable<string> SplitPath(string path) => path.Split(';', StringSplitOptions.RemoveEmptyEntries);

    private bool IsAppDir(string entry) =>
        string.Equals(Path.TrimEndingDirectorySeparator(entry.Trim()), Path.TrimEndingDirectorySeparator(env.AppDir), PathComparison);

    private void PlanSkill(List<UninstallItem> items, List<string> skipped)
    {
        var status = env.Skill.Status();
        switch (status.State)
        {
            case SkillState.UpToDate:
            case SkillState.Outdated:
                items.Add(new UninstallItem(UninstallKind.Skill, env.Skill.FilePath, "the radar-second-brain skill"));
                break;
            case SkillState.NotInstalled:
                break;
            default:
                skipped.Add($"Skill {SecondBrainSkill.Name}: left alone, it is {status.State.ToString().ToLowerInvariant()} (not an untouched copy of ours).");
                break;
        }
    }

    /// <summary>The helper the real command uses on Windows: waits until this process has exited, then removes the folder.</summary>
    public static void DeleteAfterExit(string appDir)
    {
        var script = $"ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{appDir}\"";
        if (RootToRemove(appDir) is { } root) script += $" & rmdir \"{root}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", "/c " + script) { CreateNoWindow = true, UseShellExecute = false });
    }
}
