using System.Net.Http.Json;
using System.Text.Json;

namespace Radar.Server.Tests;

public class AppVersionTests : IDisposable
{
    private readonly ServerFactory _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task Session_reports_the_app_version_without_a_revision_suffix()
    {
        var s = await _f.CreateClient().GetFromJsonAsync<JsonElement>("/api/session");
        var version = s.GetProperty("version").GetString()!;
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
        Assert.DoesNotContain('+', version);
        Assert.False(string.IsNullOrEmpty(s.GetProperty("token").GetString()));
    }
}
