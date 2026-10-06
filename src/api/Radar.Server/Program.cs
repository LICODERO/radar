using System.Diagnostics;
using System.Text.Json;
using Radar.Scanner;
using Radar.Server.Features.Agents;
using Radar.Server.Features.Files;
using Radar.Server.Features.Gaps;
using Radar.Server.Features.Items;
using Radar.Server.Features.LocalFile;
using Radar.Server.Features.Scans;
using Radar.Server.Features.Session;
using Radar.Server.Features.Settings;
using Radar.Server.Features.Skills;
using Radar.Server.Features.Vault;
using Radar.Server.Features.Visibility;
using Radar.Server.Infrastructure.Security;
using Radar.Server.Infrastructure.Storage;
using Radar.Server.Infrastructure.Localization;

var builder = WebApplication.CreateBuilder(args);

// Local-only server: never listen on anything but the loopback interface.
builder.WebHost.UseUrls(builder.Configuration["Radar:Url"] ?? "http://127.0.0.1:5178");
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

// Resolved lazily so test/host configuration added after this point is honoured.
builder.Services.AddSingleton(sp => new JsonFileStore(AppPaths.DataDir(sp.GetRequiredService<IConfiguration>())));
builder.Services.AddSingleton<IScanStore>(sp => sp.GetRequiredService<JsonFileStore>());
builder.Services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<JsonFileStore>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<RequestMessages>();
builder.Services.AddSingleton<LatestScanCache>();
builder.Services.AddSingleton<SessionToken>();
builder.Services.AddSingleton<ScanManager>();
builder.Services.AddSingleton<IFolderPicker>(FolderPickerFactory.ForCurrentOs());
builder.Services.AddSingleton<ITerminalLauncher>(sp => TerminalLauncherFactory.ForCurrentOs(sp.GetRequiredService<IConfiguration>()["Radar:Terminal"]));
builder.Services.AddSingleton<IToolLocator, PathToolLocator>();
builder.Services.AddSingleton<IToolVersionProbe, ProcessToolVersionProbe>();
builder.Services.AddSingleton<ToolStatusService>();
builder.Services.AddSingleton<IAgentGenerator, ClaudeCliGenerator>();
builder.Services.AddSingleton<GenerationGate>();
builder.Services.AddSingleton<VaultService>();
builder.Services.AddSingleton(sp => new SecondBrainSkill(AppPaths.ClaudeDir(sp.GetRequiredService<IConfiguration>())));

var app = builder.Build();

app.UseMiddleware<ApiGuard>();

var api = app.MapGroup("/api");

api.MapSession();
api.MapSettings();
api.MapScans();
api.MapFiles();
api.MapGaps();
api.MapAgents();
api.MapVisibility();
api.MapSkills();
api.MapItems();
api.MapLocalFile();
api.MapVault();

// ---- static UI ------------------------------------------------------------------------------
// The Angular build is served as static files (copied to wwwroot by run.sh).
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

if (app.Configuration.GetValue("Radar:OpenBrowser", true))
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var url = app.Urls.FirstOrDefault() ?? "http://127.0.0.1:5178";
        try
        {
            if (OperatingSystem.IsMacOS()) Process.Start("open", url);
            else if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else Process.Start("xdg-open", url);
        }
        catch { /* the URL is printed in the log anyway */ }
    });
}

app.Run();

// Exposed for integration tests.
public partial class Program;
