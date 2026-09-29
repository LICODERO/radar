using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Radar.Server;

namespace Radar.Server.Tests;

public sealed class FakePicker(bool supported, string? result) : IFolderPicker
{
    public bool Supported => supported;
    public Task<string?> PickAsync(CancellationToken ct) => Task.FromResult(result);
}

public class ServerFactoryBase : WebApplicationFactory<Program>
{
    public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "radar-data-" + Guid.NewGuid().ToString("N"));
    public IFolderPicker? Picker { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Radar:DataDir"] = DataDir,
            ["Radar:OpenBrowser"] = "false"
        }));
        if (Picker is not null)
            builder.ConfigureServices(s => s.AddSingleton(Picker));
    }

    public async Task<HttpClient> AuthedClientAsync()
    {
        var client = CreateClient();
        var session = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/session");
        client.DefaultRequestHeaders.Add("X-Radar-Token", session.GetProperty("token").GetString());
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(DataDir, true); } catch { /* best effort */ }
    }
}

public sealed class ServerFactory : ServerFactoryBase;
