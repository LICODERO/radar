using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Radar.Server.Features.Vault;

public enum SkillState { NotInstalled, UpToDate, Outdated, Modified, Foreign, Linked }

public sealed record SkillStatus(SkillState State, string Path, int AvailableVersion, int? InstalledVersion);

public enum SkillOutcome { Installed, Updated, AlreadyInstalled, UpdateNeeded, Modified, Foreign, Linked, WriteFailed }

/// <summary>
/// Installs the bundled <c>radar-second-brain</c> skill into Claude Code's user-level skills folder. The text is embedded in the server.
/// The installed file ends with a marker line holding the skill version and a hash of the text above it; that is how an untouched,
/// older copy (safe to update) is told apart from one the user edited or from a skill that is not ours. Nothing is ever overwritten
/// except an untouched older copy, and only when the caller confirms the update.
/// </summary>
public sealed partial class SecondBrainSkill(string claudeDir)
{
    public const string Name = "radar-second-brain";
    public const int Version = 1;
    private const string MarkerPrefix = "<!-- radar-skill v";

    private string SkillsDir => Path.Combine(claudeDir, "skills");
    private string SkillDir => Path.Combine(SkillsDir, Name);
    public string FilePath => Path.Combine(SkillDir, "SKILL.md");

    public SkillStatus Status()
    {
        // a link anywhere on the way (for instance skills/ pointing into an ai-toolkit checkout) means the skill is managed elsewhere
        if (IsLink(SkillsDir) || IsLink(SkillDir) || IsLink(FilePath)) return Make(SkillState.Linked, null);
        if (!File.Exists(FilePath)) return Make(SkillState.NotInstalled, null);

        string text;
        try { text = File.ReadAllText(FilePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Make(SkillState.Foreign, null); }

        var at = text.LastIndexOf(MarkerPrefix, StringComparison.Ordinal);
        if (at < 0) return Make(SkillState.Foreign, null);
        var lineEnd = text.IndexOf('\n', at);
        var line = (lineEnd < 0 ? text[at..] : text[at..lineEnd]).Trim();
        var match = MarkerLine().Match(line);
        if (!match.Success || !int.TryParse(match.Groups["v"].Value, out var installed)) return Make(SkillState.Foreign, null);

        // anything after the marker line, or a body that no longer matches its hash, means the user edited the file
        var trailing = lineEnd < 0 ? string.Empty : text[(lineEnd + 1)..];
        if (!string.IsNullOrWhiteSpace(trailing) || Hash(text[..at]) != match.Groups["h"].Value) return Make(SkillState.Modified, installed);
        return Make(installed < Version ? SkillState.Outdated : SkillState.UpToDate, installed);
    }

    public SkillOutcome Install(bool update)
    {
        var status = Status();
        switch (status.State)
        {
            case SkillState.Linked: return SkillOutcome.Linked;
            case SkillState.Foreign: return SkillOutcome.Foreign;
            case SkillState.Modified: return SkillOutcome.Modified;
            case SkillState.UpToDate: return SkillOutcome.AlreadyInstalled;
            case SkillState.Outdated when !update: return SkillOutcome.UpdateNeeded;
        }

        try
        {
            Directory.CreateDirectory(SkillDir);
            var mode = status.State == SkillState.NotInstalled ? FileMode.CreateNew : FileMode.Create;
            using var fs = new FileStream(FilePath, mode, FileAccess.Write, FileShare.None);
            fs.Write(new UTF8Encoding(false).GetBytes(Render()));
        }
        catch (IOException) when (File.Exists(FilePath) && status.State == SkillState.NotInstalled) { return SkillOutcome.Foreign; } // appeared meanwhile
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return SkillOutcome.WriteFailed; }
        return status.State == SkillState.Outdated ? SkillOutcome.Updated : SkillOutcome.Installed;
    }

    /// <summary>The skill text as it is written to disk: the bundled body plus the version/hash marker line.</summary>
    public static string Render()
    {
        var body = VaultTemplates.Read("SKILL.md");
        return body + $"{MarkerPrefix}{Version} sha256:{Hash(body)} -->\n";
    }

    private SkillStatus Make(SkillState state, int? installed) => new(state, FilePath, Version, installed);

    private static bool IsLink(string path) => new FileInfo(path).LinkTarget is not null;

    private static string Hash(string body) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body.Replace("\r\n", "\n"))));

    [GeneratedRegex(@"^<!-- radar-skill v(?<v>\d+) sha256:(?<h>[0-9a-f]{64}) -->$")]
    private static partial Regex MarkerLine();
}
