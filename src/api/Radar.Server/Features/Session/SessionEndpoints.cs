using Radar.Server.Infrastructure.Security;
namespace Radar.Server.Features.Session;

/// <summary>The app version from the build (`Directory.Build.props`), without the source revision suffix.</summary>
public static class AppVersion
{
    public static string Current { get; } = Read();

    private static string Read()
    {
        var info = typeof(AppVersion).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
        var version = (info ?? typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0").Split('+')[0];
        return version;
    }
}

public static class SessionEndpoints
{
    public static void MapSession(this RouteGroupBuilder api)
    {
        api.MapGet("/health", () => Results.Ok(new { status = "ok", app = "R.A.D.A.R" }));
        api.MapGet("/session", (SessionToken t) => Results.Ok(new { token = t.Value, version = AppVersion.Current }));
    }
}
