using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Radar.Server.Features.Relations;

/// <summary>
/// One rule of the flow between two repos: <c>From</c> (the consumer) talks to <c>To</c> (the provider). Repos are named, never
/// addressed by path, so a rule can be committed. Secrets are never stored: <c>AuthEnv</c> holds only the names of the variables or
/// settings that carry them.
/// </summary>
/// <param name="Kind">rest | graphql | grpc | events | db | files | other</param>
/// <param name="Auth">none | api-key | oauth-client | bearer | basic | mtls | other</param>
/// <param name="Contract">where the contract lives (an OpenAPI file, a schema, a topic name), free text</param>
/// <param name="Direction">which way the data travels, for kinds where it is not obvious: out (From sends to To) | in (From receives from To) | both. Request/response kinds (rest, graphql, grpc) always read as out: From calls To and gets an answer</param>
/// <param name="Visibility">public (committed with the repos) | private (kept out of git)</param>
public sealed partial record Relation(
    string From,
    string To,
    string Kind,
    string Auth,
    IReadOnlyList<string>? AuthEnv,
    string? Contract,
    string? Notes,
    string Visibility,
    string Direction = "out")
{
    public static readonly string[] Kinds = ["rest", "graphql", "grpc", "events", "db", "files", "other"];
    public static readonly string[] Auths = ["none", "api-key", "oauth-client", "bearer", "basic", "mtls", "other"];
    public static readonly string[] Directions = ["out", "in", "both"];
    /// <summary>kinds where the caller asks and the provider answers, so the direction is the call itself</summary>
    public static readonly string[] Sync = ["rest", "graphql", "grpc"];
    public const string Public = "public";
    public const string Private = "private";

    public const int MaxRelations = 200;
    public const int MaxEnv = 8;
    public const int MaxText = 500;

    [JsonIgnore]
    public string Key => From + "\u0000" + To + "\u0000" + Kind;

    public bool Involves(string repo) => From == repo || To == repo;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.\-]{0,63}$")]
    private static partial Regex EnvName();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>The cleaned rule, or null plus the reason (a code the caller shows) when it must be refused.</summary>
    public static Relation? Clean(Relation r, out string? why)
    {
        why = null;
        if (string.IsNullOrWhiteSpace(r.From) || string.IsNullOrWhiteSpace(r.To)) { why = "repos"; return null; }
        if (string.Equals(r.From, r.To, StringComparison.Ordinal)) { why = "same-repo"; return null; }
        if (!Kinds.Contains(r.Kind)) { why = "kind"; return null; }
        if (!Auths.Contains(r.Auth)) { why = "auth"; return null; }
        if (r.Visibility is not (Public or Private)) { why = "visibility"; return null; }
        if (!Directions.Contains(r.Direction)) { why = "direction"; return null; }

        var env = (r.AuthEnv ?? []).Select(e => e.Trim()).Where(e => e.Length > 0).Distinct().ToList();
        if (env.Count > MaxEnv || env.Any(e => !EnvName().IsMatch(e))) { why = "auth-env"; return null; }
        if (r.Auth == "none" && env.Count > 0) { why = "auth-env"; return null; }

        var contract = Text(r.Contract);
        var notes = Text(r.Notes);
        if (contract is null && !string.IsNullOrWhiteSpace(r.Contract) || notes is null && !string.IsNullOrWhiteSpace(r.Notes)) { why = "text"; return null; }
        return r with { Direction = Sync.Contains(r.Kind) ? "out" : r.Direction, AuthEnv = env, Contract = contract, Notes = notes, From = r.From.Trim(), To = r.To.Trim() };
    }

    /// <summary>One line without anything that could break the marked block or its JSON fence; null when empty or too long.</summary>
    private static string? Text(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var one = Whitespace().Replace(s, " ").Trim().Replace('`', '\'');
        if (one.Length > MaxText || one.Contains("<!--", StringComparison.Ordinal) || one.Contains("-->", StringComparison.Ordinal)) return null;
        return one;
    }
}
