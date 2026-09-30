using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Radar.Server.Features.Agents;
using Radar.Server.Infrastructure.Localization;

namespace Radar.Server.Tests;

public class MessageCatalogTests
{
    [Fact]
    public void Every_message_exists_in_both_languages_with_the_same_placeholders()
    {
        foreach (var msg in Enum.GetValues<Msg>())
        {
            var pl = Messages.Get(Lang.Pl, msg, "a", "b");
            var en = Messages.Get(Lang.En, msg, "a", "b");
            Assert.False(string.IsNullOrWhiteSpace(pl), msg.ToString());
            Assert.False(string.IsNullOrWhiteSpace(en), msg.ToString());
            Assert.NotEqual(pl, en); // translated, not copied
            Assert.Equal(Placeholders(Messages.Get(Lang.Pl, msg)), Placeholders(Messages.Get(Lang.En, msg)));
        }
    }

    private static string[] Placeholders(string s) => Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).Order().ToArray();

    [Theory]
    [InlineData(null, Lang.Pl)]
    [InlineData("", Lang.Pl)]
    [InlineData("en", Lang.En)]
    [InlineData("en-GB,en;q=0.9", Lang.En)]
    [InlineData("pl-PL,pl;q=0.9,en;q=0.8", Lang.Pl)]
    [InlineData("de-DE, en;q=0.5", Lang.En)]
    [InlineData("de-DE", Lang.Pl)]
    public void Picks_the_first_supported_language_and_falls_back_to_Polish(string? header, Lang expected) =>
        Assert.Equal(expected, Messages.Parse(header));

    [Fact]
    public void Formats_arguments()
    {
        Assert.Equal("The description is too long (max 2000 characters).", Messages.Get(Lang.En, Msg.DescriptionTooLong, 2000));
    }

    [Fact]
    public void Agent_validation_errors_follow_the_language()
    {
        Assert.Contains("frontmatter", AgentValidator.Validate("text", Lang.En).Errors.Single());
        Assert.Contains("Brak poprawnego frontmattera", AgentValidator.Validate("text").Errors.Single());
    }

    [Fact]
    public void Generator_exceptions_keep_the_Polish_message_and_the_key()
    {
        var e = new GeneratorException(Msg.ClaudeExited, 502, 1, "boom");
        Assert.Equal("claude zakończył się błędem (1): boom", e.Message);
        Assert.Equal(Msg.ClaudeExited, e.Key);
        Assert.Equal("claude exited with an error (1): boom", Messages.Get(Lang.En, e.Key!.Value, e.Args));
    }
}

public class LocalizedApiTests : IDisposable
{
    private readonly ServerFactory _f = new();

    public void Dispose() => _f.Dispose();

    private async Task<string> ErrorAsync(HttpClient c, HttpRequestMessage req)
    {
        using var r = await c.SendAsync(req);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()!;
    }

    private static HttpRequestMessage PutPath(string path, string? lang)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/settings") { Content = JsonContent.Create(new { scanPath = path }) };
        if (lang is not null) req.Headers.AcceptLanguage.ParseAdd(lang);
        return req;
    }

    [Fact]
    public async Task Answers_in_the_language_the_UI_asks_for()
    {
        var c = await _f.AuthedClientAsync();
        var missing = Path.Combine(Path.GetTempPath(), "radar-nope-" + Guid.NewGuid().ToString("N"));

        Assert.Equal("The directory does not exist.", await ErrorAsync(c, PutPath(missing, "en")));
        Assert.Equal("Katalog nie istnieje.", await ErrorAsync(c, PutPath(missing, "pl")));
        Assert.Equal("Katalog nie istnieje.", await ErrorAsync(c, PutPath(missing, null))); // default
    }

    [Fact]
    public async Task Localizes_the_guard_and_the_no_scan_error()
    {
        var anon = _f.CreateClient();
        anon.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        using var denied = await anon.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal("Missing or invalid session token.", (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var c = await _f.AuthedClientAsync();
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        using var noScan = await c.GetAsync("/api/gaps");
        Assert.Equal("No saved scan.", (await noScan.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }
}
