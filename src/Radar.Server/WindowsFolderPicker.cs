using System.Diagnostics;

namespace Radar.Server;

/// <summary>Native folder dialog on Windows (System.Windows.Forms through PowerShell).</summary>
public sealed class WindowsFolderPicker : IFolderPicker
{
    private const string Script = @"
Add-Type -AssemblyName System.Windows.Forms
$d = New-Object System.Windows.Forms.FolderBrowserDialog
$d.Description = 'Wybierz katalog z repozytoriami'
$d.ShowNewFolderButton = $false
if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    Write-Output $d.SelectedPath
}";

    public bool Supported => OperatingSystem.IsWindows();

    public async Task<string?> PickAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        foreach (var a in new[] { "-NoProfile", "-STA", "-EncodedCommand", TerminalCommands.PowerShellEncoded(Script) }) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Nie udało się uruchomić PowerShell.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            var output = await proc.StandardOutput.ReadToEndAsync(timeout.Token);
            await proc.WaitForExitAsync(timeout.Token);
            var path = output.Trim();
            return proc.ExitCode == 0 && path.Length > 0 ? path : null;
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(true); } catch { /* already gone */ }
            throw;
        }
    }
}

public static class FolderPickerFactory
{
    public static IFolderPicker ForCurrentOs() => OperatingSystem.IsWindows() ? new WindowsFolderPicker() : new OsaScriptFolderPicker();
}
