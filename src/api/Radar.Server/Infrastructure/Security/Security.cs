using Radar.Server.Infrastructure.Localization;

namespace Radar.Server.Infrastructure.Security;

/// <summary>
/// Protects the local API from other web pages in the user's browser:
/// only loopback Host names, same-origin (or explicitly allowed) Origin, and a per-run token for everything but /api/session.
/// </summary>
public sealed class ApiGuard(RequestDelegate next, SessionToken token, IConfiguration config)
{
    private static readonly HashSet<string> LoopbackHosts = new(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "localhost", "[::1]" };
    private readonly HashSet<string> _allowedOrigins = new(
        (config.GetSection("Radar:AllowedOrigins").Get<string[]>() ?? []).Select(o => o.TrimEnd('/')), StringComparer.OrdinalIgnoreCase);

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'";
            await next(ctx);
            return;
        }

        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";

        if (!HostAllowed(ctx)) { await Deny(ctx, 403, Msg.HostDenied); return; }
        if (!OriginAllowed(ctx)) { await Deny(ctx, 403, Msg.OriginDenied); return; }

        var open = ctx.Request.Method == HttpMethods.Get
            && (ctx.Request.Path.Equals("/api/session") || ctx.Request.Path.Equals("/api/health"));
        if (!open)
        {
            var supplied = ctx.Request.Headers["X-Radar-Token"].FirstOrDefault() ?? ctx.Request.Query["token"].FirstOrDefault();
            if (!token.Matches(supplied)) { await Deny(ctx, 401, Msg.TokenDenied); return; }
        }
        await next(ctx);
    }

    private static bool HostAllowed(HttpContext ctx)
    {
        var host = ctx.Request.Host;
        if (!host.HasValue) return false;
        if (!LoopbackHosts.Contains(host.Host)) return false;
        var localPort = ctx.Connection.LocalPort;
        return host.Port is null || localPort == 0 || host.Port == localPort;
    }

    private bool OriginAllowed(HttpContext ctx)
    {
        var origin = ctx.Request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrEmpty(origin)) return true; // non-browser clients and same-origin GETs
        origin = origin.TrimEnd('/');
        if (_allowedOrigins.Contains(origin)) return true;
        return string.Equals(origin, $"{ctx.Request.Scheme}://{ctx.Request.Host}", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task Deny(HttpContext ctx, int status, Msg message)
    {
        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new { error = Messages.Get(Messages.Parse(ctx.Request.Headers.AcceptLanguage.ToString()), message) });
    }
}

public sealed class SessionToken
{
    public string Value { get; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    public bool Matches(string? candidate) =>
        candidate is not null && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(candidate), System.Text.Encoding.UTF8.GetBytes(Value));
}
