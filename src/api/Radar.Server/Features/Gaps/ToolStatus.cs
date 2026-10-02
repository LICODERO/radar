using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Radar.Server.Features.Gaps;

public sealed record ToolStatus(string Tool, bool Found, string? Path, string? Version);

public interface IToolVersionProbe
{
    /// <summary>Runs `<paramref name="exe"/> --version`; null when it fails, times out or prints nothing usable.</summary>
    Task<string?> VersionAsync(string exe, CancellationToken ct);
}

/// <summary>Reads the version a tool prints for `--version` (e.g. "2.1.5 (Claude Code)" gives "2.1.5"). Nothing else is run, no arguments come from the client.</summary>
public sealed partial class ProcessToolVersionProbe : IToolVersionProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<string?> VersionAsync(string exe, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        try
        {
            var psi = new ProcessStartInfo(exe, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            p.StandardInput.Close();
            var stdout = p.StandardOutput.ReadToEndAsync(cts.Token);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { p.Kill(true); } catch { /* already gone */ }
                throw;
            }
            return p.ExitCode == 0 ? Parse(await stdout) : null;
        }
        catch (Exception e) when (e is OperationCanceledException or System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            ct.ThrowIfCancellationRequested();
            return null;
        }
    }

    public static string? Parse(string? output)
    {
        var m = SemVer().Match(output ?? string.Empty);
        return m.Success ? m.Value : null;
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.\-]+)?")]
    private static partial Regex SemVer();
}

/// <summary>Finds a tool and asks it for its version. The answer is cached for a minute so a UI that polls does not spawn a process every time; `force` skips the cache.</summary>
public sealed class ToolStatusService(IToolLocator locator, IToolVersionProbe probe)
{
    /// <summary>The AI CLIs the status badge reports, with the executables that count for each (first one found wins). Only `claude` and `codex` can be run from the app.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Executables = new Dictionary<string, string[]>
    {
        ["claude"] = ["claude"],
        ["codex"] = ["codex"],
        ["cursor"] = ["cursor-agent", "cursor"],
        ["antigravity"] = ["antigravity"]
    };

    /// <summary>Desktop apps that count as installed when no command is on PATH (the version is then unknown).</summary>
    private static readonly IReadOnlyDictionary<string, string> AppBundles = new Dictionary<string, string>
    {
        ["cursor"] = "Cursor.app",
        ["antigravity"] = "Antigravity.app"
    };

    public static bool IsKnown(string? tool) => tool is not null && Executables.ContainsKey(tool);

    public async Task<IReadOnlyList<ToolStatus>> AllAsync(bool force, CancellationToken ct) =>
        await Task.WhenAll(Executables.Keys.Select(t => GetAsync(t, force, ct)));

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);
    private readonly Dictionary<string, (DateTimeOffset At, ToolStatus Status)> _cache = [];
    private readonly object _lock = new();

    public async Task<ToolStatus> GetAsync(string tool, bool force, CancellationToken ct)
    {
        lock (_lock)
        {
            if (!force && _cache.TryGetValue(tool, out var hit) && DateTimeOffset.UtcNow - hit.At < Ttl) return hit.Status;
        }

        var path = Executables[tool].Select(locator.Find).FirstOrDefault(p => p is not null);
        var app = path is null && AppBundles.TryGetValue(tool, out var bundle) ? locator.FindApp(bundle) : null;
        var status = path is not null ? new ToolStatus(tool, true, path, await probe.VersionAsync(path, ct))
            : app is not null ? new ToolStatus(tool, true, app, null)
            : new ToolStatus(tool, false, null, null);

        lock (_lock) _cache[tool] = (DateTimeOffset.UtcNow, status);
        return status;
    }
}
