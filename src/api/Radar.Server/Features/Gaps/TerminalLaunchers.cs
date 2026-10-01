using System.Diagnostics;
using System.Text;

namespace Radar.Server.Features.Gaps;

public sealed record LaunchRequest(string Dir, string Tool, string Prompt, string Title);

public interface ITerminalLauncher
{
    bool Supported { get; }

    /// <summary>posix | powershell: the shell flavour of commands that users paste themselves.</summary>
    string Shell { get; }

    string Platform { get; }

    /// <summary>The terminal app that will open, for display (null when it is just "the terminal").</summary>
    string? App => null;

    /// <summary>Opens a terminal window in <see cref="LaunchRequest.Dir"/> running the tool with the prompt. Returns once the window has been requested.</summary>
    Task LaunchAsync(LaunchRequest request, CancellationToken ct);
}

public static class Tools
{
    public static readonly string[] Known = ["claude", "codex"];
    public static bool IsKnown(string? tool) => tool is not null && Known.Contains(tool, StringComparer.Ordinal);
}

/// <summary>Pure command builders (unit tested on every OS, even though each launcher only runs on its own).</summary>
public static class TerminalCommands
{
    public static string Posix(string s) => "'" + s.Replace("'", "'\\''") + "'";
    public static string PowerShell(string s) => "'" + s.Replace("'", "''") + "'";
    public static string AppleScript(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void Check(LaunchRequest r)
    {
        if (!Tools.IsKnown(r.Tool)) throw new ArgumentException("Unknown tool", nameof(r));
    }

    /// <summary>`cd '<dir>' && claude '<prompt>'` for a POSIX shell.</summary>
    public static string PosixCommand(LaunchRequest r)
    {
        Check(r);
        return $"cd {Posix(r.Dir)} && {r.Tool} {Posix(r.Prompt)}";
    }

    /// <summary>Arguments for `osascript`: opens a Terminal.app window and runs the command in it.</summary>
    public static string[] MacOsaArguments(LaunchRequest r) =>
    [
        "-e", "tell application \"Terminal\"",
        "-e", "activate",
        "-e", $"do script {AppleScript(PosixCommand(r))}",
        "-e", "end tell"
    ];

    /// <summary>
    /// Arguments for `osascript`: opens an iTerm2 window with the default profile and types the command into it. When iTerm2 was not
    /// running it opens a window of its own at start-up, so that one is used instead of adding a second, empty one.
    /// </summary>
    public static string[] ITermOsaArguments(LaunchRequest r) =>
    [
        "-e", "set wasRunning to application \"iTerm\" is running",
        "-e", "tell application \"iTerm\"",
        "-e", "activate",
        "-e", "if not wasRunning then",
        "-e", "repeat 30 times",
        "-e", "if (count of windows) > 0 then exit repeat",
        "-e", "delay 0.1",
        "-e", "end repeat",
        "-e", "end if",
        "-e", "if (count of windows) is 0 or wasRunning then",
        "-e", "set targetWindow to (create window with default profile)",
        "-e", "else",
        "-e", "set targetWindow to current window",
        "-e", "end if",
        "-e", "tell current session of targetWindow",
        "-e", $"write text {AppleScript(PosixCommand(r))}",
        "-e", "end tell",
        "-e", "end tell"
    ];

    public static string PowerShellScript(LaunchRequest r)
    {
        Check(r);
        var title = new string(r.Title.Where(c => !char.IsControl(c)).ToArray());
        return string.Join("\n",
            "$ErrorActionPreference = 'Stop'",
            $"$Host.UI.RawUI.WindowTitle = {PowerShell(title)}",
            $"Set-Location -LiteralPath {PowerShell(r.Dir)}",
            $"$prompt = {PowerShell(r.Prompt)}",
            $"& {r.Tool} $prompt");
    }

    /// <summary>Base64 (UTF-16LE) for `powershell -EncodedCommand`, which avoids every command-line quoting problem.</summary>
    public static string PowerShellEncoded(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
}

public enum MacTerminalApp { Terminal, ITerm }

public static class MacTerminalChoice
{
    /// <summary>
    /// <paramref name="setting"/> is `terminal`, `iterm` or `auto` (also when empty): auto picks iTerm2 when it is installed,
    /// because a developer who has it almost always works there, and Terminal.app otherwise.
    /// </summary>
    public static MacTerminalApp Resolve(string? setting, Func<string, bool> exists) => (setting ?? "").Trim().ToLowerInvariant() switch
    {
        "terminal" => MacTerminalApp.Terminal,
        "iterm" or "iterm2" => MacTerminalApp.ITerm,
        _ => exists("/Applications/iTerm.app") || exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", "iTerm.app"))
            ? MacTerminalApp.ITerm : MacTerminalApp.Terminal
    };
}

public sealed class MacTerminalLauncher(MacTerminalApp app = MacTerminalApp.Terminal) : ITerminalLauncher
{
    public bool Supported => true;
    public string Shell => "posix";
    public string Platform => "macos";
    public string? App => app == MacTerminalApp.ITerm ? "iTerm2" : "Terminal.app";

    public async Task LaunchAsync(LaunchRequest request, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("osascript") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        var args = app == MacTerminalApp.ITerm ? TerminalCommands.ITermOsaArguments(request) : TerminalCommands.MacOsaArguments(request);
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start osascript.");
        // the first run makes macOS ask the user to allow controlling Terminal, so allow a generous wait
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var err = await proc.StandardError.ReadToEndAsync(timeout.Token);
        await proc.WaitForExitAsync(timeout.Token);
        if (proc.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(err) ? $"{App} did not start." : err.Trim());
    }
}

public sealed class WindowsTerminalLauncher : ITerminalLauncher
{
    public bool Supported => true;
    public string Shell => "powershell";
    public string Platform => "windows";

    public Task LaunchAsync(LaunchRequest request, CancellationToken ct)
    {
        var encoded = TerminalCommands.PowerShellEncoded(TerminalCommands.PowerShellScript(request));
        // `start` gives the new PowerShell its own console window that stays open (-NoExit)
        var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "/c", "start", "", "powershell.exe", "-NoExit", "-NoProfile", "-EncodedCommand", encoded }) psi.ArgumentList.Add(a);
        Process.Start(psi)?.Dispose();
        return Task.CompletedTask;
    }
}

public sealed class UnsupportedTerminalLauncher : ITerminalLauncher
{
    public bool Supported => false;
    public string Shell => "posix";
    public string Platform => OperatingSystem.IsLinux() ? "linux" : "other";
    public Task LaunchAsync(LaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
}

public static class TerminalLauncherFactory
{
    /// <param name="terminalSetting">`Radar:Terminal` (macOS): terminal, iterm or auto</param>
    public static ITerminalLauncher ForCurrentOs(string? terminalSetting = null) =>
        OperatingSystem.IsMacOS() ? new MacTerminalLauncher(MacTerminalChoice.Resolve(terminalSetting, Directory.Exists))
        : OperatingSystem.IsWindows() ? new WindowsTerminalLauncher()
        : new UnsupportedTerminalLauncher();
}
