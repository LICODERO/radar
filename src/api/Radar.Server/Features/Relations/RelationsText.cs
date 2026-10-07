using System.Text;
using System.Text.Json;
using Radar.Scanner;

namespace Radar.Server.Features.Relations;

/// <summary>
/// The texts RADAR writes for the flow rules. <c>.claude/relations.md</c> (and <c>.claude/relations.local.md</c> for private rules) hold
/// the rules a repo takes part in: readable text for Claude plus one JSON fence RADAR reads back. CLAUDE.md / CLAUDE.local.md only get a
/// short pointer, because both are loaded into every session. English, because Claude reads it.
/// </summary>
public static class RelationsText
{
    public const string FileStart = "<!-- radar:relations -->";
    public const string FileEnd = "<!-- /radar:relations -->";
    public const string RefStart = "<!-- radar:relations-ref -->";
    public const string RefEnd = "<!-- /radar:relations-ref -->";
    public const string PublicFile = ".claude/relations.md";
    public const string PrivateFile = ".claude/relations.local.md";

    private const string Fence = "```";

    private static readonly Dictionary<string, string> KindNames = new()
    {
        ["rest"] = "REST API", ["graphql"] = "GraphQL", ["grpc"] = "gRPC", ["events"] = "events / message bus",
        ["db"] = "shared database", ["files"] = "files / storage", ["other"] = "other"
    };

    private static readonly Dictionary<string, string> AuthNames = new()
    {
        ["none"] = "none", ["api-key"] = "API key", ["oauth-client"] = "OAuth client credentials (client id + secret)",
        ["bearer"] = "bearer token / JWT", ["basic"] = "basic auth", ["mtls"] = "mutual TLS", ["other"] = "other"
    };

    /// <summary>The block of a relations file: the rules this repo takes part in (calls or is called by).</summary>
    public static string File(string repo, IReadOnlyList<Relation> rules)
    {
        var sb = new StringBuilder();
        sb.Append("# Flow between repos\n\n");
        sb.Append("Rules for how `").Append(repo).Append("` exchanges data with other repos. Names only; secrets are never stored here, ")
          .Append("only the names of the variables that hold them.\n");
        Section(sb, "This repo calls", rules.Where(r => r.From == repo), r => $"→ `{r.To}`");
        Section(sb, "Called by other repos", rules.Where(r => r.To == repo), r => $"← `{r.From}`");
        sb.Append("\n").Append(Fence).Append("json\n").Append(JsonSerializer.Serialize(rules, RadarJson.Options)).Append('\n').Append(Fence);
        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string title, IEnumerable<Relation> rules, Func<Relation, string> head)
    {
        var list = rules.ToList();
        if (list.Count == 0) return;
        sb.Append("\n## ").Append(title).Append('\n');
        foreach (var r in list)
        {
            sb.Append("\n### ").Append(head(r)).Append(" (").Append(KindNames[r.Kind]).Append(")\n");
            if (!Relation.Sync.Contains(r.Kind)) sb.Append("- Data flow: `").Append(r.From).Append("` ").Append(r.Direction switch { "in" => "←", "both" => "↔", _ => "→" }).Append(" `").Append(r.To).Append("`\n");
            sb.Append("- Auth: ").Append(AuthNames[r.Auth]);
            if (r.AuthEnv is { Count: > 0 }) sb.Append(". Held in: ").Append(string.Join(", ", r.AuthEnv.Select(e => "`" + e + "`")));
            sb.Append('\n');
            if (r.Contract is not null) sb.Append("- Contract: ").Append(r.Contract).Append('\n');
            if (r.Notes is not null) sb.Append("- Notes: ").Append(r.Notes).Append('\n');
        }
    }

    /// <summary>The rules of the JSON fence inside a relations file, or an empty list when there is none or it is unreadable.</summary>
    public static IReadOnlyList<Relation> Read(string? fileText)
    {
        if (fileText is null) return [];
        var s = fileText.IndexOf(FileStart, StringComparison.Ordinal);
        var e = fileText.IndexOf(FileEnd, StringComparison.Ordinal);
        if (s < 0 || e < s) return [];
        var block = fileText[s..e].Replace("\r\n", "\n");
        var open = block.IndexOf(Fence + "json\n", StringComparison.Ordinal);
        if (open < 0) return [];
        open += Fence.Length + 5;
        var close = block.IndexOf("\n" + Fence, open, StringComparison.Ordinal);
        if (close < 0) return [];
        try
        {
            var list = JsonSerializer.Deserialize<List<Relation>>(block[open..close], RadarJson.Options) ?? [];
            var clean = new List<Relation>();
            foreach (var r in list.Take(Relation.MaxRelations))
                if (r is not null && Relation.Clean(r, out _) is { } ok) clean.Add(ok);
            return clean;
        }
        catch (JsonException) { return []; }
    }

    /// <summary>The pointer kept in CLAUDE.md (public rules) or CLAUDE.local.md (private rules).</summary>
    public static string Pointer(string repo, IReadOnlyList<Relation> rules, string file)
    {
        var parts = rules.Select(r => r.From == repo ? $"`{r.To}` (calls it)" : $"`{r.From}` (calls this repo)").Distinct().ToList();
        var sb = new StringBuilder();
        sb.Append("## Flow between repos\n\n");
        sb.Append("This repo is connected to: ").Append(string.Join(", ", parts)).Append(". Rules: `").Append(file).Append("`.\n");
        sb.Append("Before changing an API, event, authentication or data shape that crosses a repo, read that file. For each repo it names, ")
          .Append("look at that repo (usually the sibling folder `../<name>`), work through its agent, tell the user what has to change there ")
          .Append("and ask before editing anything in it.");
        return sb.ToString();
    }
}
