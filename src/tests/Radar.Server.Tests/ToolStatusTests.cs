using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Radar.Server.Features.Gaps;

namespace Radar.Server.Tests;

public class ToolStatusTests
{
    [Theory]
    [InlineData("2.1.5 (Claude Code)\n", "2.1.5")]
    [InlineData("codex-cli 0.46.0", "0.46.0")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.2")]
    [InlineData("no version here", null)]
    [InlineData("", null)]
    public void Parse_picks_the_version_out_of_the_output(string output, string? expected) =>
        Assert.Equal(expected, ProcessToolVersionProbe.Parse(output));

    private sealed class CountingProbe : IToolVersionProbe
    {
        public int Calls;
        public Task<string?> VersionAsync(string exe, CancellationToken ct) { Calls++; return Task.FromResult<string?>("9.9.9"); }
    }

    private sealed class Missing : IToolLocator
    {
        public string? Find(string tool) => null;
    }

    [Fact]
    public async Task Missing_tool_is_reported_without_running_anything()
    {
        var probe = new CountingProbe();
        var status = await new ToolStatusService(new Missing(), probe).GetAsync("claude", false, default);
        Assert.False(status.Found);
        Assert.Null(status.Version);
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task Answer_is_cached_until_refresh_is_forced()
    {
        var probe = new CountingProbe();
        var service = new ToolStatusService(new FoundEverything(), probe);
        var first = await service.GetAsync("claude", false, default);
        await service.GetAsync("claude", false, default);
        Assert.Equal(1, probe.Calls);
        await service.GetAsync("claude", true, default);
        Assert.Equal(2, probe.Calls);
        Assert.Equal(new ToolStatus("claude", true, "/fake/claude", "9.9.9"), first);
    }

    [Fact]
    public void A_broken_symlink_on_the_path_is_skipped_instead_of_failing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "radar-broken-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.CreateSymbolicLink(Path.Combine(dir, "claude"), Path.Combine(dir, "does-not-exist"));
            Assert.Null(new PathToolLocator([dir]).Find("claude"));
        }
        finally { Directory.Delete(dir, true); }
    }

    private sealed class OnlyCursorAgent : IToolLocator
    {
        public string? Find(string tool) => tool == "cursor-agent" ? "/fake/cursor-agent" : null;
    }

    [Fact]
    public async Task Cursor_is_found_by_its_cursor_agent_executable_and_all_three_are_reported()
    {
        var all = await new ToolStatusService(new OnlyCursorAgent(), new CountingProbe()).AllAsync(false, default);
        Assert.Equal(["claude", "codex", "cursor", "antigravity"], all.Select(x => x.Tool));
        Assert.Equal([false, false, true, false], all.Select(x => x.Found));
        Assert.Equal("/fake/cursor-agent", all[2].Path);
    }

    private sealed class OnlyApps : IToolLocator
    {
        public string? Find(string tool) => null;
        public string? FindApp(string bundle) => bundle == "Antigravity.app" ? "/Applications/Antigravity.app" : null;
    }

    [Fact]
    public async Task An_installed_app_without_a_command_counts_as_installed_with_unknown_version()
    {
        var probe = new CountingProbe();
        var status = await new ToolStatusService(new OnlyApps(), probe).GetAsync("antigravity", false, default);
        Assert.Equal(new ToolStatus("antigravity", true, "/Applications/Antigravity.app", null), status);
        Assert.Equal(0, probe.Calls);
    }

    private sealed class WebFactory : ServerFactoryBase
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s =>
            {
                s.AddSingleton<IToolLocator, FoundEverything>();
                s.AddSingleton<IToolVersionProbe>(new CountingProbe());
            });
        }
    }

    [Fact]
    public async Task Endpoint_returns_status_for_a_known_tool_and_rejects_others()
    {
        using var f = new WebFactory();
        var c = await f.AuthedClientAsync();
        var ok = await c.GetFromJsonAsync<JsonElement>("/api/tools/claude");
        Assert.True(ok.GetProperty("found").GetBoolean());
        Assert.Equal("9.9.9", ok.GetProperty("version").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/tools/rm")).StatusCode);
        var all = await c.GetFromJsonAsync<JsonElement>("/api/tools/status");
        Assert.Equal(4, all.GetArrayLength());
    }
}
