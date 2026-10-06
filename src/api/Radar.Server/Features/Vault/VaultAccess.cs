using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Radar.Scanner;
using Radar.Server.Features.Visibility;

namespace Radar.Server.Features.Vault;

/// <param name="Action">create | update | unchanged</param>
/// <param name="Blocked">null, or why it cannot be done: no-git | tracked | exclude-damaged | settings-invalid | forbidden</param>
/// <param name="ExcludeEntry">the line that keeps the settings file out of git, when it is not ignored yet</param>
public sealed record AccessPlan(string Path, string Action, string? Blocked, string? ExcludeEntry);

/// <summary>
/// Lets Claude read the vault without a prompt in one repo: the vault folder goes into <c>permissions.additionalDirectories</c> of the repo's
/// <c>.claude/settings.local.json</c> (personal, never shared). Claude Code only keeps that file out of git when it creates it itself, so a file
/// created here is first put on RADAR's private list. Everything else in the file is kept; a file that is not plain JSON is left alone.
/// </summary>
public static class VaultAccess
{
    public const string RelativePath = ".claude/settings.local.json";
    private const int MaxBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Write = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static AccessPlan Plan(string repoDir, string vaultPath)
    {
        var file = FilePath(repoDir);
        var claudeDir = Path.Combine(repoDir, ".claude");
        if (Directory.Exists(file) || (Directory.Exists(claudeDir) && !SafeFs.IsInside(repoDir, claudeDir)) || !SafeFs.IsInside(repoDir, file))
            return new AccessPlan(RelativePath, "create", "forbidden", null);

        var exists = File.Exists(file);
        if (exists && new FileInfo(file).Length > MaxBytes) return new AccessPlan(RelativePath, "update", "settings-invalid", null);
        var (root, error) = Load(file);
        if (error) return new AccessPlan(RelativePath, "update", "settings-invalid", null);
        if (Contains(root, vaultPath)) return new AccessPlan(RelativePath, "unchanged", null, null);

        var guard = PrivateFileGuard.Check(repoDir, RelativePath);
        return new AccessPlan(RelativePath, exists ? "update" : "create", guard.Blocked, guard.ExcludeEntry);
    }

    /// <summary>Adds the vault to the repo's local settings. Call after <see cref="Plan"/> returned no block; restores what it changed if a step fails.</summary>
    public static void Apply(string repoDir, string vaultPath, AccessPlan plan)
    {
        if (plan.Action == "unchanged") return;
        var file = FilePath(repoDir);
        var undo = new List<Action>();
        try
        {
            if (plan.ExcludeEntry is not null)
            {
                var exclude = PrivateFileGuard.ExcludeFile(repoDir);
                var old = exclude is not null && File.Exists(exclude) ? File.ReadAllText(exclude) : null;
                PrivateFileGuard.Apply(repoDir, RelativePath);
                if (exclude is not null) undo.Add(() => Restore(exclude, old));
            }

            var oldText = File.Exists(file) ? File.ReadAllText(file) : null;
            var (root, error) = Load(file);
            if (error) throw new IOException("settings changed meanwhile");
            var permissions = root["permissions"] as JsonObject ?? new JsonObject();
            var dirs = permissions["additionalDirectories"] as JsonArray ?? new JsonArray();
            dirs.Add(Normalize(vaultPath));
            permissions["additionalDirectories"] = dirs;
            root["permissions"] = permissions;

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var tmp = file + ".radar-tmp";
            File.WriteAllText(tmp, root.ToJsonString(Write) + "\n", new UTF8Encoding(false));
            File.Move(tmp, file, overwrite: true);
            undo.Add(() => Restore(file, oldText));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            for (var i = undo.Count - 1; i >= 0; i--) undo[i]();
            throw new VaultAccessFailure();
        }
    }

    private static string FilePath(string repoDir) => Path.Combine(repoDir, ".claude", "settings.local.json");

    /// <summary>The settings object (empty when there is no file) and whether the file is unusable (not JSON, not an object, or permissions of an unexpected shape).</summary>
    private static (JsonObject Root, bool Error) Load(string file)
    {
        if (!File.Exists(file)) return (new JsonObject(), false);
        try
        {
            if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject root) return (new JsonObject(), true);
            if (root["permissions"] is { } p && p is not JsonObject) return (root, true);
            if (root["permissions"] is JsonObject po && po["additionalDirectories"] is { } d && (d is not JsonArray arr || arr.Any(x => x is null || x.GetValueKind() != JsonValueKind.String))) return (root, true);
            return (root, false);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return (new JsonObject(), true); }
    }

    private static bool Contains(JsonObject root, string vaultPath)
    {
        var want = Normalize(vaultPath);
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return (root["permissions"] as JsonObject)?["additionalDirectories"] is JsonArray a && a.Any(x => string.Equals(Normalize(x!.GetValue<string>()), want, cmp));
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(path);

    private static void Restore(string path, string? old)
    {
        try
        {
            if (old is null) File.Delete(path);
            else File.WriteAllText(path, old, new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* best effort */ }
    }
}

public sealed class VaultAccessFailure : Exception;
