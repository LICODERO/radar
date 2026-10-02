using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Radar.Server.Tests;

public class ScanToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "radar-tool-" + Guid.NewGuid().ToString("N"));
    private readonly ServerFactoryBase _f = new();

    public ScanToolTests() => Directory.CreateDirectory(Path.Combine(_root, "api", ".git"));

    public void Dispose() { _f.Dispose(); try { Directory.Delete(_root, true); } catch { /* best effort */ } }

    private async Task<HttpClient> ClientAsync()
    {
        var c = await _f.AuthedClientAsync();
        await c.PutAsJsonAsync("/api/settings", new { scanPath = _root });
        return c;
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("cursor")]
    [InlineData("antigravity")]
    [InlineData("rm -rf")]
    public async Task A_tool_the_scanner_does_not_support_yet_is_refused(string tool)
    {
        var c = await ClientAsync();
        var resp = await c.PostAsJsonAsync("/api/scans", new { tool });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("Claude", (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("claude")]
    [InlineData(null)]
    public async Task Claude_or_no_tool_starts_a_scan(string? tool)
    {
        var c = await ClientAsync();
        var resp = tool is null ? await c.PostAsync("/api/scans", null) : await c.PostAsJsonAsync("/api/scans", new { tool });
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
    }
}
