using Microsoft.Extensions.Configuration;
using Radar.Server.Features.Vault;
using Radar.Server.Infrastructure.Storage;

namespace Radar.Server.Features.Uninstall;

/// <summary>
/// <c>radar uninstall</c>: shows what removing R.A.D.A.R. would delete and, with <c>--confirm</c>, deletes it. It runs instead of the
/// server (no port is opened). Output is plain English because a coding agent reads it as often as a person does.
/// </summary>
public static class UninstallCommand
{
    private const string Usage = """
        Usage: radar uninstall [--data] [--skill] [--confirm]

          (no flags)  show what would be removed: the program folder and the radar link / PATH entry. Nothing is deleted.
          --data      also remove the app's settings and the last scan
          --skill     also remove the radar-second-brain skill, when it is an untouched copy of ours
          --confirm   really delete what is listed
        """;

    public static int Run(string[] args, TextWriter output)
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in args)
        {
            if (a is "--data" or "--skill" or "--confirm") flags.Add(a);
            else if (a is "-h" or "--help") { output.WriteLine(Usage); return 0; }
            else { output.WriteLine($"Unknown option: {a}\n\n{Usage}"); return 2; }
        }

        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var binDir = Environment.GetEnvironmentVariable("RADAR_BIN_DIR");
        var windows = OperatingSystem.IsWindows();
        var env = new UninstallEnvironment(
            AppDir: Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
            BinDir: string.IsNullOrWhiteSpace(binDir) ? Path.Combine(AppPaths.Home, ".local", "bin") : AppPaths.Expand(binDir),
            DataDir: AppPaths.DataDir(config),
            Skill: new SecondBrainSkill(AppPaths.ClaudeDir(config)),
            Windows: windows,
            ReadUserPath: windows ? () => Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) : null,
            WriteUserPath: windows ? p => Environment.SetEnvironmentVariable("Path", p, EnvironmentVariableTarget.User) : null,
            DeleteAppDir: windows ? Uninstaller.DeleteAfterExit : null);
        return Run(env, flags.Contains("--data"), flags.Contains("--skill"), flags.Contains("--confirm"), output);
    }

    public static int Run(UninstallEnvironment env, bool data, bool skill, bool confirm, TextWriter output)
    {
        var uninstaller = new Uninstaller(env);
        var plan = uninstaller.Plan(data, skill);

        output.WriteLine(confirm ? "Removing:" : "Would remove:");
        if (plan.Items.Count == 0) output.WriteLine("  (nothing)");
        foreach (var i in plan.Items) output.WriteLine($"  {AppPaths.Display(i.Path)}  ({i.Detail})");
        foreach (var s in plan.Skipped) output.WriteLine($"Skipped: {s}");

        if (!confirm)
        {
            output.WriteLine("\nNothing was deleted. Run again with --confirm to remove the items above.");
            WriteLeftAlone(output);
            return 0;
        }

        var failures = uninstaller.Apply(plan);
        foreach (var f in failures) output.WriteLine($"Could not remove {f}");
        if (failures.Count > 0) return 1;

        output.WriteLine(env.Windows && plan.Items.Any(i => i.Kind == UninstallKind.App)
            ? "\nDone. The program folder is removed a moment after this window closes."
            : "\nDone.");
        WriteLeftAlone(output);
        return 0;
    }

    private static void WriteLeftAlone(TextWriter output)
    {
        output.WriteLine("""

            Left alone on purpose:
              - your repositories, including anything R.A.D.A.R. wrote there after you confirmed it (.claude files, the radar blocks in
                CLAUDE.md / CLAUDE.local.md and in .git/info/exclude),
              - the second-brain vault folder (your notes),
              - your shell profile: a PATH line you added for radar stays until you remove it.
            Quit a running R.A.D.A.R. before uninstalling.
            """);
    }
}
