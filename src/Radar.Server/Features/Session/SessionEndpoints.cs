namespace Radar.Server;

public static class SessionEndpoints
{
    public static void MapSession(this RouteGroupBuilder api)
    {
        api.MapGet("/health", () => Results.Ok(new { status = "ok", app = "R.A.D.A.R" }));
        api.MapGet("/session", (SessionToken t) => Results.Ok(new { token = t.Value }));
    }
}
