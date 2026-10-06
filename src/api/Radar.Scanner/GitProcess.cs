using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Radar.Scanner;

/// <summary>Runs the git CLI in a repository. Returns null when git is missing, hangs or cannot be started; callers treat that as "unknown".</summary>
public static class GitProcess
{
    public sealed record Output(int ExitCode, string Text, string Error = "");

    public static Output? Run(string repoDir, IEnumerable<string> args, string? stdin = null, int timeoutMs = 5000)
    {
        try
        {
            var info = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin is not null,
                StandardOutputEncoding = Encoding.UTF8,
            };
            info.ArgumentList.Add("-C");
            info.ArgumentList.Add(repoDir);
            foreach (var arg in args) info.ArgumentList.Add(arg);

            using var process = Process.Start(info);
            if (process is null) return null;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (stdin is not null)
            {
                process.StandardInput.Write(stdin);
                process.StandardInput.Close();
            }
            if (!process.WaitForExit(timeoutMs) || !stdout.Wait(timeoutMs) || !stderr.Wait(timeoutMs))
            {
                try { process.Kill(true); } catch (InvalidOperationException) { /* already gone */ }
                return null;
            }
            return new Output(process.ExitCode, stdout.Result, stderr.Result);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException) { return null; }
    }
}
