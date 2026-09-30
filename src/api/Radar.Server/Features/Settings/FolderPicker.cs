using System.Diagnostics;

namespace Radar.Server.Features.Settings;

public interface IFolderPicker
{
    bool Supported { get; }

    /// <summary>Shows a native folder dialog. Returns null when the user cancels.</summary>
    Task<string?> PickAsync(CancellationToken ct);
}

/// <summary>Native folder dialog on macOS through osascript; other platforms fall back to manual entry in the UI.</summary>
public sealed class OsaScriptFolderPicker : IFolderPicker
{
    public bool Supported => OperatingSystem.IsMacOS();

    public async Task<string?> PickAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo("osascript")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add("POSIX path of (choose folder with prompt \"Wybierz katalog z repozytoriami\")");

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start osascript.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            var output = await proc.StandardOutput.ReadToEndAsync(timeout.Token);
            await proc.WaitForExitAsync(timeout.Token);
            if (proc.ExitCode != 0) return null; // user cancelled (-128)
            var path = output.Trim();
            return path.Length == 0 ? null : path;
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(true); } catch { /* already gone */ }
            throw;
        }
    }
}
