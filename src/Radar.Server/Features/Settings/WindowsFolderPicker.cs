using System.Diagnostics;
using Radar.Server.Features.Gaps;

namespace Radar.Server.Features.Settings;

/// <summary>Native folder dialog on Windows (System.Windows.Forms through PowerShell).</summary>
public sealed class WindowsFolderPicker : IFolderPicker
{
    // The path is returned as base64 (UTF-8) so console encoding, BOM and non-ASCII characters cannot corrupt it.
    // The dialog gets a topmost owner window, otherwise it can open behind the browser when started without a console.
    private const string Script = @"
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$owner = New-Object System.Windows.Forms.Form
$owner.TopMost = $true
$owner.ShowInTaskbar = $false
$owner.StartPosition = 'CenterScreen'
$owner.Size = New-Object System.Drawing.Size(0, 0)
$owner.Show()
$owner.Activate()
try {
    $d = New-Object System.Windows.Forms.FolderBrowserDialog
    $d.Description = 'Wybierz katalog z repozytoriami'
    $d.ShowNewFolderButton = $false
    if ($d.ShowDialog($owner) -eq [System.Windows.Forms.DialogResult]::OK) {
        [Console]::Out.Write([Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($d.SelectedPath)))
    }
} finally {
    $owner.Close()
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
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var a in new[] { "-NoProfile", "-STA", "-EncodedCommand", TerminalCommands.PowerShellEncoded(Script) }) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Nie udało się uruchomić PowerShell.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            var output = await proc.StandardOutput.ReadToEndAsync(timeout.Token);
            await proc.WaitForExitAsync(timeout.Token);
            if (proc.ExitCode != 0)
            {
                var error = (await proc.StandardError.ReadToEndAsync(timeout.Token)).Trim();
                throw new InvalidOperationException($"Okno wyboru folderu nie otworzyło się (PowerShell, kod {proc.ExitCode}): {error}");
            }
            var encoded = output.Trim().Trim('\uFEFF');
            if (encoded.Length == 0) return null; // user cancelled
            var path = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Trim();
            return path.Length == 0 ? null : path;
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
