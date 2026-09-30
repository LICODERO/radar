using System.Diagnostics;
using System.Text.Json;
using System.Text;
using Radar.Server.Features.Gaps;
using Radar.Server.Infrastructure.Localization;

namespace Radar.Server.Features.Agents;

public sealed record AgentGenerationRequest(string Description, string Stack, IReadOnlyList<string> ExistingAgents);

public sealed record GeneratedText(string Text, decimal? CostUsd);

public sealed class GeneratorException : Exception
{
    /// <summary>A ready message (shown as is).</summary>
    public GeneratorException(string message, int status) : base(message) => Status = status;

    /// <summary>A catalogued message: the endpoint renders it in the request language; <see cref="Exception.Message"/> is the Polish text.</summary>
    public GeneratorException(Msg key, int status, params object[] args) : base(Messages.Get(Lang.Pl, key, args))
    {
        Status = status;
        Key = key;
        Args = args;
    }

    public int Status { get; }
    public Msg? Key { get; }
    public object[] Args { get; } = [];
}

public interface IAgentGenerator
{
    Task<GeneratedText> GenerateAsync(AgentGenerationRequest request, CancellationToken ct);
}

/// <summary>
/// Drafts an agent file with a one-shot `claude -p` call that has no tools, runs in an empty temporary directory
/// (never in the repo) and receives only the description, the stack name and the existing agent names.
/// </summary>
public sealed class ClaudeCliGenerator(IToolLocator locator, IConfiguration config) : IAgentGenerator
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(150);

    public async Task<GeneratedText> GenerateAsync(AgentGenerationRequest request, CancellationToken ct)
    {
        var exe = config["Radar:ClaudePath"] ?? locator.Find("claude")
            ?? throw new GeneratorException(Msg.ClaudeNotFound, StatusCodes.Status503ServiceUnavailable);

        var workDir = Path.Combine(Path.GetTempPath(), "radar-gen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            return await RunAsync(exe, workDir, AgentPrompt.Build(request.Description, request.Stack, request.ExistingAgents), ct);
        }
        finally
        {
            try { Directory.Delete(workDir, true); } catch { /* best effort */ }
        }
    }

    private async Task<GeneratedText> RunAsync(string exe, string workDir, string prompt, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-p", "--output-format", "json", "--tools", "", "--no-session-persistence", "--disable-slash-commands",
            // do not load the user's MCP servers and user-level settings: they add ~12k tokens (about 5 cents) and
            // send private configuration along with every call; the call runs in an empty temp dir, so only "project" is harmless
            "--strict-mcp-config", "--setting-sources", "project",
            "--max-budget-usd", "0.5", "--system-prompt", AgentPrompt.SystemPrompt
        };
        var model = config["Radar:AgentModel"];
        if (!string.IsNullOrWhiteSpace(model)) { args.Add("--model"); args.Add(model); }

        // npm installs on Windows expose claude as a .cmd shim, which needs cmd.exe to run
        var viaCmd = OperatingSystem.IsWindows() && (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));
        var psi = new ProcessStartInfo(viaCmd ? "cmd.exe" : exe)
        {
            WorkingDirectory = workDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (viaCmd) { psi.ArgumentList.Add("/c"); psi.ArgumentList.Add(exe); }
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new GeneratorException(Msg.ClaudeStartFailed, StatusCodes.Status502BadGateway);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        try
        {
            var stdout = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = proc.StandardError.ReadToEndAsync(cts.Token);
            await proc.StandardInput.WriteAsync(prompt.AsMemory(), cts.Token);
            proc.StandardInput.Close();
            await proc.WaitForExitAsync(cts.Token);
            var output = await stdout;
            var error = await stderr;

            if (proc.ExitCode != 0)
                throw new GeneratorException(Msg.ClaudeExited, StatusCodes.Status502BadGateway, proc.ExitCode, Trim(error.Length > 0 ? error : output));
            return Parse(output);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(true); } catch { /* already gone */ }
            if (ct.IsCancellationRequested) throw;
            throw new GeneratorException(Msg.ClaudeTimeout, StatusCodes.Status504GatewayTimeout);
        }
    }

    public static GeneratedText Parse(string output)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array) // stream-json style: take the last result object
                root = root.EnumerateArray().LastOrDefault(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("result", out _));
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("result", out var result))
                throw new GeneratorException(Msg.ClaudeUnexpected, StatusCodes.Status502BadGateway);

            var text = result.ValueKind == JsonValueKind.String ? result.GetString() ?? "" : result.ToString();
            if (root.TryGetProperty("is_error", out var isErr) && isErr.ValueKind == JsonValueKind.True)
                throw new GeneratorException(Msg.ClaudeReportedError, StatusCodes.Status502BadGateway, Trim(text));

            decimal? cost = null;
            foreach (var key in new[] { "total_cost_usd", "cost_usd" })
                if (root.TryGetProperty(key, out var c) && c.ValueKind == JsonValueKind.Number) { cost = c.GetDecimal(); break; }
            return new GeneratedText(text, cost);
        }
        catch (JsonException)
        {
            // not JSON: accept plain text output
            return string.IsNullOrWhiteSpace(output)
                ? throw new GeneratorException(Msg.ClaudeEmpty, StatusCodes.Status502BadGateway)
                : new GeneratedText(output, null);
        }
    }

    private static string Trim(string s)
    {
        var t = s.Trim();
        return t.Length > 300 ? t[..300] + "…" : t;
    }
}

/// <summary>Only one draft is generated at a time (it costs money and blocks a process).</summary>
public sealed class GenerationGate
{
    private readonly SemaphoreSlim _sem = new(1, 1);
    public bool TryEnter() => _sem.Wait(0);
    public void Leave() => _sem.Release();
}
