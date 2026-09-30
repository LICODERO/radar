using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Radar.Server;

public sealed record AgentGenerationRequest(string Description, string Stack, IReadOnlyList<string> ExistingAgents);

public sealed record GeneratedText(string Text, decimal? CostUsd);

public sealed class GeneratorException(string message, int status) : Exception(message)
{
    public int Status { get; } = status;
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
            ?? throw new GeneratorException("Nie znaleziono polecenia claude w PATH serwera. Zainstaluj Claude Code i zaloguj się.", StatusCodes.Status503ServiceUnavailable);

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

        using var proc = Process.Start(psi) ?? throw new GeneratorException("Nie udało się uruchomić claude.", StatusCodes.Status502BadGateway);
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
                throw new GeneratorException($"claude zakończył się błędem ({proc.ExitCode}): {Trim(error.Length > 0 ? error : output)}", StatusCodes.Status502BadGateway);
            return Parse(output);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(true); } catch { /* already gone */ }
            if (ct.IsCancellationRequested) throw;
            throw new GeneratorException("claude nie odpowiedział w ciągu 150 s.", StatusCodes.Status504GatewayTimeout);
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
                throw new GeneratorException("Nieoczekiwana odpowiedź claude.", StatusCodes.Status502BadGateway);

            var text = result.ValueKind == JsonValueKind.String ? result.GetString() ?? "" : result.ToString();
            if (root.TryGetProperty("is_error", out var isErr) && isErr.ValueKind == JsonValueKind.True)
                throw new GeneratorException($"claude zgłosił błąd: {Trim(text)}", StatusCodes.Status502BadGateway);

            decimal? cost = null;
            foreach (var key in new[] { "total_cost_usd", "cost_usd" })
                if (root.TryGetProperty(key, out var c) && c.ValueKind == JsonValueKind.Number) { cost = c.GetDecimal(); break; }
            return new GeneratedText(text, cost);
        }
        catch (JsonException)
        {
            // not JSON: accept plain text output
            return string.IsNullOrWhiteSpace(output)
                ? throw new GeneratorException("Pusta odpowiedź claude.", StatusCodes.Status502BadGateway)
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
