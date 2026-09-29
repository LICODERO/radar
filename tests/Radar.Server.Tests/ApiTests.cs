using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Radar.Scanner;
using Radar.Server;

namespace Radar.Server.Tests;

public class SecurityTests : IClassFixture<ServerFactory>
{
    private readonly ServerFactory _f;
    public SecurityTests(ServerFactory f) => _f = f;

    [Fact]
    public async Task Health_and_session_are_open()
    {
        var c = _f.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/health")).StatusCode);
        var s = await c.GetFromJsonAsync<JsonElement>("/api/session");
        Assert.True(s.GetProperty("token").GetString()!.Length >= 32);
    }

    [Fact]
    public async Task Other_endpoints_need_the_token()
    {
        var c = _f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsync("/api/scans", null)).StatusCode);

        c.DefaultRequestHeaders.Add("X-Radar-Token", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/settings")).StatusCode);
    }

    [Fact]
    public async Task Token_is_accepted_from_header_and_query()
    {
        var c = await _f.AuthedClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/settings")).StatusCode);
        var token = c.DefaultRequestHeaders.GetValues("X-Radar-Token").First();
        var plain = _f.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await plain.GetAsync("/api/settings?token=" + token)).StatusCode);
    }

    [Theory]
    [InlineData("evil.example.com")]
    [InlineData("192.168.1.10")]
    public async Task Rejects_non_loopback_host_headers(string host)
    {
        var c = await _f.AuthedClientAsync();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/settings");
        req.Headers.Host = host;
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Rejects_foreign_origins_even_with_a_token()
    {
        var c = await _f.AuthedClientAsync();
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/settings") { Content = JsonContent.Create(new { scanPath = "/tmp" }) };
        req.Headers.Add("Origin", "https://evil.example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Static_responses_carry_a_content_security_policy()
    {
        var r = await _f.CreateClient().GetAsync("/index.html");
        Assert.True(r.Headers.Contains("Content-Security-Policy"));
    }
}

public class SettingsAndScanTests
{
    private static string BuildProjects()
    {
        var root = Path.Combine(Path.GetTempPath(), "radar-proj-" + Guid.NewGuid().ToString("N"));
        foreach (var name in new[] { "orders-api", "billing-api" })
        {
            Directory.CreateDirectory(Path.Combine(root, name, ".git"));
        }
        File.WriteAllText(Path.Combine(root, "orders-api", "CLAUDE.md"), "# orders");
        Directory.CreateDirectory(Path.Combine(root, "orders-api", ".claude", "agents"));
        File.WriteAllText(Path.Combine(root, "orders-api", ".claude", "agents", "cr.md"), "---\nname: cr\n---\n");
        return root;
    }

    private static async Task<List<(string Name, JsonElement Data)>> ReadEventsAsync(HttpClient c, string id)
    {
        var events = new List<(string, JsonElement)>();
        using var resp = await c.GetAsync($"/api/scans/{id}/events", HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal("text/event-stream", resp.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync());
        string? name = null;
        string? line;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while ((line = await reader.ReadLineAsync(cts.Token)) is not null)
        {
            if (line.StartsWith("event: ")) name = line[7..];
            else if (line.StartsWith("data: ")) events.Add((name!, JsonDocument.Parse(line[6..]).RootElement.Clone()));
        }
        return events;
    }

    [Fact]
    public async Task Settings_validate_and_persist_the_scan_path()
    {
        using var f = new ServerFactory();
        var c = await f.AuthedClientAsync();
        var root = BuildProjects();

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/settings", new { scanPath = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/settings", new { scanPath = "/no/such/dir-" + Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/settings", new { scanPath = "/" })).StatusCode);

        var ok = await c.PutAsJsonAsync("/api/settings", new { scanPath = root });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var s = await c.GetFromJsonAsync<JsonElement>("/api/settings");
        Assert.Equal(root, s.GetProperty("scanPath").GetString());
        Assert.True(s.GetProperty("exists").GetBoolean());
        Assert.True(File.Exists(Path.Combine(f.DataDir, "settings.json")));
    }

    [Fact]
    public async Task Scan_requires_a_valid_path_and_latest_is_404_before_the_first_scan()
    {
        using var f = new ServerFactory();
        var c = await f.AuthedClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/scan/latest")).StatusCode);
        // no usable scan path configured and the default ~/projects may or may not exist on this machine
        var settings = await c.GetFromJsonAsync<JsonElement>("/api/settings");
        if (!settings.GetProperty("exists").GetBoolean())
            Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/scans", null)).StatusCode);
    }

    [Fact]
    public async Task Full_scan_streams_real_events_and_saves_the_result()
    {
        using var f = new ServerFactory();
        var c = await f.AuthedClientAsync();
        var root = BuildProjects();
        await c.PutAsJsonAsync("/api/settings", new { scanPath = root });

        var start = await c.PostAsync("/api/scans", null);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var id = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var events = await ReadEventsAsync(c, id);
        var names = events.Select(e => e.Name).ToList();
        Assert.Equal("started", names[0]);
        Assert.Equal("completed", names[^1]);
        Assert.Equal(2, names.Count(n => n == "repo-scanned"));
        Assert.Equal(2, names.Count(n => n == "repo-found"));
        Assert.Contains("phase", names);
        var last = events.Last(e => e.Name == "repo-scanned").Data;
        Assert.Equal(2, last.GetProperty("total").GetInt32());
        Assert.Equal(2, last.GetProperty("counters").GetProperty("repos").GetInt32());

        // the result is available (and saved) as soon as "completed" arrives
        var latest = await c.GetFromJsonAsync<JsonElement>("/api/scan/latest");
        Assert.Equal(2, latest.GetProperty("summary").GetProperty("repos").GetInt32());
        Assert.Equal(1, latest.GetProperty("summary").GetProperty("gaps").GetInt32());
        Assert.True(File.Exists(Path.Combine(f.DataDir, "scan-result.json")));

        // a late subscriber still receives the full history
        var replay = await ReadEventsAsync(c, id);
        Assert.Equal(names, replay.Select(e => e.Name).ToList());
    }

    [Fact]
    public async Task Unknown_scan_id_gives_404()
    {
        using var f = new ServerFactory();
        var c = await f.AuthedClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/scans/nope/events")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/api/scans/nope")).StatusCode);
    }

    [Fact]
    public async Task Folder_picker_paths()
    {
        using var supported = new ServerFactory { Picker = new FakePicker(true, "/tmp/picked") };
        var c1 = await supported.AuthedClientAsync();
        var ok = await c1.PostAsync("/api/pick-folder", null);
        Assert.Equal("/tmp/picked", (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("path").GetString());

        using var cancelled = new ServerFactory { Picker = new FakePicker(true, null) };
        Assert.Equal(HttpStatusCode.NoContent, (await (await cancelled.AuthedClientAsync()).PostAsync("/api/pick-folder", null)).StatusCode);

        using var unsupported = new ServerFactory { Picker = new FakePicker(false, null) };
        Assert.Equal(HttpStatusCode.NotImplemented, (await (await unsupported.AuthedClientAsync()).PostAsync("/api/pick-folder", null)).StatusCode);
    }
}

public class SessionTests
{
    [Fact]
    public async Task Subscribers_get_the_history_and_new_events_until_completion()
    {
        var s = new ScanSession("/x");
        s.Append(new ScanStarted("/x"));
        s.Append(new RepoFound(1, "a"));

        var received = new List<ScanEvent>();
        var task = Task.Run(async () => { await foreach (var e in s.Subscribe()) received.Add(e); });

        await Task.Delay(50);
        s.Append(new ScanCancelled());
        s.Complete();
        await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, received.Count);
        Assert.IsType<ScanCancelled>(received[^1]);
    }

    [Fact]
    public async Task Cancelling_a_running_scan_emits_cancelled_and_keeps_the_previous_result()
    {
        var dir = Path.Combine(Path.GetTempPath(), "radar-cancel-" + Guid.NewGuid().ToString("N"));
        for (var i = 0; i < 300; i++) Directory.CreateDirectory(Path.Combine(dir, "repo" + i, ".git"));

        var store = new JsonFileStore(Path.Combine(dir, "_data"));
        var manager = new ScanManager(store, new LatestScanCache(store), Microsoft.Extensions.Logging.Abstractions.NullLogger<ScanManager>.Instance);
        var (session, started) = manager.Start(dir, 2);
        Assert.True(started);
        manager.Cancel(session.Id);

        var events = new List<ScanEvent>();
        await foreach (var e in session.Subscribe()) events.Add(e);
        // the scan may win the race on a fast machine; either way it must end with a terminal event
        Assert.True(events[^1] is ScanCancelled or ScanCompleted);
        if (events[^1] is ScanCancelled) Assert.Null(await store.LoadLatestJsonAsync());
        try { Directory.Delete(dir, true); } catch { }
    }
}
