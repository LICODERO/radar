using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Radar.Server.Features.Gaps;
using Radar.Server;

namespace Radar.Server.Tests;

public class TerminalCommandTests
{
    private static LaunchRequest Req(string dir = "/p/repo", string prompt = "do it", string tool = "claude") => new(dir, tool, prompt, "title");

    private static readonly string[] Nasty =
    [
        "it's a \"test\"", "$(rm -rf /) `id` ; && | > <", "zażółć gęślą jaźń", "back\\slash", "line1\nline2", "%PATH% ^& !x!"
    ];

    [Fact]
    public void Posix_quoting_wraps_and_escapes_single_quotes()
    {
        Assert.Equal("'abc'", TerminalCommands.Posix("abc"));
        Assert.Equal("'O'\\''Brien'", TerminalCommands.Posix("O'Brien"));
    }

    [Fact]
    public void Posix_command_changes_into_the_repo_and_starts_the_tool()
    {
        Assert.Equal("cd '/p/repo' && claude 'do it'", TerminalCommands.PosixCommand(Req()));
        Assert.Equal("cd '/p/my repo' && codex 'x'", TerminalCommands.PosixCommand(Req("/p/my repo", "x", "codex")));
    }

    [Fact]
    public void Posix_command_survives_a_real_shell_round_trip()
    {
        if (OperatingSystem.IsWindows()) return;
        foreach (var prompt in Nasty.Where(p => !p.Contains('\n')))
        {
            var cmd = TerminalCommands.PosixCommand(Req("/tmp", prompt)).Replace("&& claude", "&& printf %s");
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(cmd);
            using var p = System.Diagnostics.Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            Assert.Equal(prompt, output);
        }
    }

    [Fact]
    public void Mac_arguments_run_the_posix_command_inside_terminal_app()
    {
        var args = TerminalCommands.MacOsaArguments(Req("/p/O'Brien", "say \"hi\" \\ there"));
        Assert.Contains("tell application \"Terminal\"", args);
        var doScript = args.Single(a => a.StartsWith("do script "));
        // AppleScript string: backslashes and double quotes escaped
        Assert.Contains("say \\\"hi\\\" \\\\ there", doScript);
        Assert.Contains("O'\\\\''Brien", doScript);
    }

    [Fact]
    public void Unknown_tools_are_refused_before_anything_is_built()
    {
        Assert.Throws<ArgumentException>(() => TerminalCommands.PosixCommand(Req(tool: "rm -rf /")));
        Assert.Throws<ArgumentException>(() => TerminalCommands.PowerShellScript(Req(tool: "calc")));
    }

    [Fact]
    public void PowerShell_script_sets_the_location_and_passes_the_prompt_as_one_argument()
    {
        var script = TerminalCommands.PowerShellScript(Req("C:\\Users\\O'Brien\\repo", "it's fine"));
        Assert.Contains("Set-Location -LiteralPath 'C:\\Users\\O''Brien\\repo'", script);
        Assert.Contains("$prompt = 'it''s fine'", script);
        Assert.EndsWith("& claude $prompt", script);
    }

    [Fact]
    public void PowerShell_encoded_command_is_utf16_base64_and_round_trips()
    {
        foreach (var prompt in Nasty)
        {
            var script = TerminalCommands.PowerShellScript(Req("C:\\repo", prompt));
            var encoded = TerminalCommands.PowerShellEncoded(script);
            Assert.Matches("^[A-Za-z0-9+/=]+$", encoded);   // no character that needs command-line quoting
            Assert.Equal(script, Encoding.Unicode.GetString(Convert.FromBase64String(encoded)));
        }
    }

    [Fact]
    public void PowerShell_titles_cannot_break_out_of_their_quotes()
    {
        var script = TerminalCommands.PowerShellScript(new LaunchRequest("C:\\r", "claude", "p", "a'b\nc"));
        Assert.Contains("WindowTitle = 'a''bc'", script);
    }
}

public class ToolLocatorTests
{
    [Fact]
    public void Finds_an_executable_on_the_given_path_only()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Path.Combine(Path.GetTempPath(), "radar-bin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var exe = Path.Combine(dir, "claude");
            File.WriteAllText(exe, "#!/bin/sh\n");
            File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            File.WriteAllText(Path.Combine(dir, "codex"), "not executable");
            File.SetUnixFileMode(Path.Combine(dir, "codex"), UnixFileMode.UserRead | UnixFileMode.UserWrite);

            IToolLocator loc = new PathToolLocator([dir, "/definitely/not/here"]);
            Assert.True(loc.IsAvailable("claude"));
            Assert.False(loc.IsAvailable("codex"));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public sealed class RecordingLauncher(bool supported = true) : ITerminalLauncher
{
    public List<LaunchRequest> Launched { get; } = [];
    public bool Supported => supported;
    public string Shell => "posix";
    public string Platform => "test";
    public Task LaunchAsync(LaunchRequest request, CancellationToken ct) { Launched.Add(request); return Task.CompletedTask; }
}

public sealed class FoundEverything : IToolLocator
{
    public string? Find(string tool) => "/fake/" + tool;
}

public class RunEndpointTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "radar-run-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingLauncher _launcher;
    private readonly WebFactory _f;
    private HttpClient _c = null!;

    private sealed class WebFactory(RecordingLauncher launcher) : ServerFactoryBase
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s =>
            {
                s.AddSingleton<ITerminalLauncher>(launcher);
                s.AddSingleton<IToolLocator, FoundEverything>();
            });
        }
    }

    public RunEndpointTests() : this(true) { }
    protected RunEndpointTests(bool supported)
    {
        _launcher = new RecordingLauncher(supported);
        _f = new WebFactory(_launcher);
    }

    private async Task ScanAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "billing-api", ".git"));
        Directory.CreateDirectory(Path.Combine(_root, "orders-api", ".git"));
        File.WriteAllText(Path.Combine(_root, "orders-api", "CLAUDE.md"), "# x");
        _c = await _f.AuthedClientAsync();
        await _c.PutAsJsonAsync("/api/settings", new { scanPath = _root });
        var id = (await (await _c.PostAsync("/api/scans", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        using var resp = await _c.GetAsync($"/api/scans/{id}/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (await reader.ReadLineAsync(cts.Token) is not null) { }
    }

    private Task<HttpResponseMessage> Run(object body) => _c.PostAsJsonAsync("/api/run", body);

    [Fact]
    public async Task Gaps_endpoint_returns_prompts_built_on_the_server()
    {
        await ScanAsync();
        var items = await _c.GetFromJsonAsync<JsonElement>("/api/gaps");
        var first = items.EnumerateArray().First(i => i.GetProperty("type").GetString() == "no-claude-md");
        Assert.Equal("billing-api", first.GetProperty("repoId").GetString());
        Assert.Contains("create a CLAUDE.md", first.GetProperty("prompt").GetString());
        Assert.Equal(Path.Combine(_root, "billing-api"), first.GetProperty("dir").GetString());
    }

    [Fact]
    public async Task Launches_the_terminal_with_a_command_built_from_the_scan()
    {
        await ScanAsync();
        var r = await Run(new { repoId = "billing-api", type = "no-claude-md", tool = "claude" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var req = Assert.Single(_launcher.Launched);
        Assert.Equal("claude", req.Tool);
        Assert.Equal(Path.Combine(_root, "billing-api"), req.Dir);
        Assert.Contains("CLAUDE.md", req.Prompt);
        Assert.True((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("toolFound").GetBoolean());
    }

    [Theory]
    [InlineData("nope", "no-claude-md", "claude", HttpStatusCode.NotFound)]
    [InlineData("../billing-api", "no-claude-md", "claude", HttpStatusCode.NotFound)]
    [InlineData("orders-api", "no-claude-md", "claude", HttpStatusCode.BadRequest)]   // orders-api has a CLAUDE.md
    [InlineData("billing-api", "made-up", "claude", HttpStatusCode.BadRequest)]
    [InlineData("billing-api", "no-claude-md", "bash", HttpStatusCode.BadRequest)]
    [InlineData("billing-api", "no-claude-md", "claude; rm -rf /", HttpStatusCode.BadRequest)]
    public async Task Refuses_invalid_requests_without_launching(string repo, string type, string tool, HttpStatusCode expected)
    {
        await ScanAsync();
        Assert.Equal(expected, (await Run(new { repoId = repo, type, tool })).StatusCode);
        Assert.Empty(_launcher.Launched);
    }

    [Fact]
    public async Task Ignores_any_command_text_sent_by_the_client()
    {
        await ScanAsync();
        var r = await Run(new { repoId = "billing-api", type = "no-claude-md", tool = "claude", prompt = "rm -rf ~", command = "rm -rf ~", dir = "/" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var req = Assert.Single(_launcher.Launched);
        Assert.DoesNotContain("rm -rf", req.Prompt);
        Assert.Equal(Path.Combine(_root, "billing-api"), req.Dir);
    }

    [Fact]
    public async Task Needs_the_token()
    {
        await ScanAsync();
        var anon = _f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/run", new { repoId = "billing-api", type = "no-claude-md", tool = "claude" })).StatusCode);
        Assert.Empty(_launcher.Launched);
    }

    [Fact]
    public async Task Tools_endpoint_reports_the_platform_and_available_tools()
    {
        _c = await _f.AuthedClientAsync();
        var t = await _c.GetFromJsonAsync<JsonElement>("/api/tools");
        Assert.True(t.GetProperty("canLaunch").GetBoolean());
        Assert.Equal("posix", t.GetProperty("shell").GetString());
        Assert.True(t.GetProperty("tools").GetProperty("claude").GetBoolean());
    }

    public void Dispose()
    {
        _f.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }
}

public class UnsupportedRunTests
{
    [Fact]
    public async Task Answers_501_when_the_system_cannot_open_a_terminal()
    {
        using var f = new ServerFactoryWithLauncher(new RecordingLauncher(false));
        var c = await f.AuthedClientAsync();
        var r = await c.PostAsJsonAsync("/api/run", new { repoId = "a", type = "no-claude-md", tool = "claude" });
        Assert.Equal(HttpStatusCode.NotImplemented, r.StatusCode);
        var t = await c.GetFromJsonAsync<JsonElement>("/api/tools");
        Assert.False(t.GetProperty("canLaunch").GetBoolean());
    }

    private sealed class ServerFactoryWithLauncher(ITerminalLauncher launcher) : ServerFactoryBase
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddSingleton(launcher));
        }
    }
}
