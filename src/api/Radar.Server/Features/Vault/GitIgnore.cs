using System.ComponentModel;
using System.Diagnostics;

namespace Radar.Server.Features.Vault;

public static class GitIgnore
{
    /// <summary>Whether git ignores the path in the repository (true/false), or null when git could not tell.</summary>
    public static bool? IsIgnored(string repoDir, string relativePath)
    {
        try
        {
            var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-C", repoDir, "check-ignore", "-q", "--", relativePath }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info);
            if (process is null) return null;
            if (!process.WaitForExit(3000))
            {
                try { process.Kill(true); } catch (InvalidOperationException) { /* already gone */ }
                return null;
            }
            return process.ExitCode switch { 0 => true, 1 => false, _ => null };
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException) { return null; }
    }
}
