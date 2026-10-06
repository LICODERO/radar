using System.Text;
using Radar.Scanner;
using Radar.Server.Features.Gaps;
using Radar.Server.Features.Vault;
using Radar.Server.Features.Visibility;
using Radar.Server.Infrastructure.Localization;

namespace Radar.Server.Features.LocalFile;

/// <param name="Id">exclude | agents-md | workflows-local | workflows-public</param>
/// <param name="File">which file it changes (repo-relative, or the git exclude file)</param>
/// <param name="Action">create | update | remove | unchanged</param>
/// <param name="Text">what is written (the block, or the exclude line)</param>
public sealed record LocalPart(string Id, string File, string Action, string? Text);

/// <param name="Blocked">null, or why nothing is written: no-git | local-tracked | exclude-damaged | local-damaged | claude-damaged | forbidden | too-big</param>
/// <param name="Notes">codes the UI turns into advice: agents-md-not-imported | no-claude-md</param>
/// <param name="PublicCandidates">public workflows not yet mentioned in CLAUDE.md (what the optional CLAUDE.md block would list)</param>
public sealed record LocalPlan(IReadOnlyList<LocalPart> Parts, string? Blocked, IReadOnlyList<string> Notes, int PublicCandidates, bool Applied);

/// <summary>
/// Keeps Claude informed about a repo's private setup: an index of private workflows and the AGENTS.md import in CLAUDE.local.md (and,
/// when the user asks, of public workflows in CLAUDE.md), and makes sure CLAUDE.local.md itself never reaches a commit. Every block is
/// marked, so only RADAR's own lines are ever rewritten. Without <c>apply</c> nothing is written.
/// </summary>
public static class LocalFileService
{
    private const int MaxBytes = 1024 * 1024;
    private const string LocalName = "CLAUDE.local.md";
    private const string ClaudeName = "CLAUDE.md";

    public static LocalPlan? Run(ScanResult scan, RepoInfo repo, bool includePublic, bool apply)
    {
        var root = Path.GetFullPath(scan.ScanRoot);
        var repoDir = GapCommands.DirOf(scan, repo);
        if (!Directory.Exists(repoDir) || !SafeFs.IsInside(root, repoDir)) return null;

        LocalPlan Blocked(string why) => new([], why, [], 0, false);

        var localPath = Path.Combine(repoDir, LocalName);
        var claudePath = Path.Combine(repoDir, ClaudeName);
        if (Directory.Exists(localPath) || !SafeFs.IsInside(repoDir, localPath)) return Blocked("forbidden");
        if (Directory.Exists(claudePath) || !SafeFs.IsInside(repoDir, claudePath)) return Blocked("forbidden");
        if ((File.Exists(localPath) && new FileInfo(localPath).Length > MaxBytes) || (File.Exists(claudePath) && new FileInfo(claudePath).Length > MaxBytes)) return Blocked("too-big");

        var localText = File.Exists(localPath) ? File.ReadAllText(localPath) : null;
        var claudeText = File.Exists(claudePath) ? File.ReadAllText(claudePath) : null;

        // what to say
        var mine = scan.Workflows
            .Select(w => (w, r: w.Repos.FirstOrDefault(x => x.RepoId == repo.Id)))
            .Where(x => x.r is not null)
            .Select(x => (Entry: new LocalBlocks.Entry(x.w.Name, x.w.Description, x.w.When, x.r!.Path), x.r!.Visibility, x.r.Linked))
            .ToList();
        var localEntries = mine.Where(x => x.Visibility is Visibilities.Private or Visibilities.Untracked).Select(x => x.Entry).ToList();
        // a public workflow that CLAUDE.md already points at needs no second mention
        var publicEntries = mine.Where(x => x.Visibility == Visibilities.Public && !(claudeText?.Contains(x.Entry.Path, StringComparison.OrdinalIgnoreCase) ?? false)).Select(x => x.Entry).ToList();
        var notes = new List<string>();

        var newLocal = localText;
        var parts = new List<LocalPart>();

        string? workflowsLocal = localEntries.Count > 0 ? LocalBlocks.Workflows(localEntries, isPrivate: true) : null;
        newLocal = Apply(newLocal, LocalBlocks.WorkflowsStart, LocalBlocks.WorkflowsEnd, workflowsLocal, out var damaged1);
        var importText = LocalBlocks.NeedsAgentsImport(repoDir) ? LocalBlocks.AgentsImport : null;
        newLocal = Apply(newLocal, LocalBlocks.AgentsStart, LocalBlocks.AgentsEnd, importText, out var damaged2);
        if (damaged1 || damaged2) return Blocked("local-damaged");
        parts.Add(Part("workflows-local", LocalName, localText, LocalBlocks.WorkflowsStart, LocalBlocks.WorkflowsEnd, workflowsLocal));
        parts.Add(Part("agents-md", LocalName, localText, LocalBlocks.AgentsStart, LocalBlocks.AgentsEnd, importText));

        if (File.Exists(Path.Combine(repoDir, "AGENTS.md")) && claudeText is not null && !claudeText.Contains("@AGENTS.md", StringComparison.Ordinal)) notes.Add("agents-md-not-imported");

        string? newClaude = claudeText;
        if (includePublic)
        {
            if (claudeText is null) notes.Add("no-claude-md");
            else
            {
                var text = publicEntries.Count > 0 ? LocalBlocks.Workflows(publicEntries, isPrivate: false) : null;
                newClaude = Apply(claudeText, LocalBlocks.WorkflowsStart, LocalBlocks.WorkflowsEnd, text, out var damaged3);
                if (damaged3) return Blocked("claude-damaged");
                parts.Add(Part("workflows-public", ClaudeName, claudeText, LocalBlocks.WorkflowsStart, LocalBlocks.WorkflowsEnd, text));
            }
        }

        // CLAUDE.local.md holds private notes: it must never be offered for commit
        var writesLocal = newLocal != localText && !string.IsNullOrWhiteSpace(newLocal);
        string? excludeEntry = null;
        if (writesLocal)
        {
            var state = GitVisibility.Resolve(repoDir, [new VisibilityQuery("l", LocalName, LocalName)])["l"];
            if (state == Visibilities.Unknown) return Blocked("no-git");
            if (state == Visibilities.Public) return Blocked("local-tracked");
            if (state == Visibilities.Untracked)
            {
                var why = ItemVisibility.Hide(repoDir, LocalName, false, apply: false);
                if (why is not null) return Blocked(why);
                excludeEntry = GitExclude.Entry(LocalName, false);
                parts.Insert(0, new LocalPart("exclude", ".git/info/exclude", "update", excludeEntry));
            }
        }

        var shown = parts.Where(p => p.Action != "unchanged").ToList();
        var plan = new LocalPlan(shown, null, notes, publicEntries.Count, false);
        if (!apply || shown.Count == 0) return plan;

        // write: exclude first (so the private file is never exposed), then the files; any failure restores what was changed
        var undo = new List<Action>();
        try
        {
            if (excludeEntry is not null)
            {
                var file = GitExclude.ResolveFile(repoDir);
                var old = file is not null && File.Exists(file) ? File.ReadAllText(file) : null;
                if (ItemVisibility.Hide(repoDir, LocalName, false, apply: true) is not null) throw new IOException("cannot hide");
                if (file is not null) undo.Add(() => Restore(file, old));
            }
            if (newLocal != localText) { Write(localPath, newLocal!); undo.Add(() => Restore(localPath, localText)); }
            if (newClaude != claudeText) { Write(claudePath, newClaude!); undo.Add(() => Restore(claudePath, claudeText)); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or VisibilityFailure)
        {
            for (var i = undo.Count - 1; i >= 0; i--) undo[i]();
            throw new LocalFileFailure();
        }
        return plan with { Applied = true };
    }

    private static string? Apply(string? text, string start, string end, string? inner, out bool damaged)
    {
        damaged = false;
        string? result;
        if (inner is not null) result = MarkedBlock.Upsert(text, start, end, inner);
        else result = MarkedBlock.Remove(text, start, end);
        if (result is null && (inner is not null || text is not null)) { damaged = true; return text; }
        return result;
    }

    /// <summary>The text inside a marked block, or null when the file has no such block.</summary>
    private static string? Inner(string? text, string start, string end)
    {
        if (text is null) return null;
        var s = text.IndexOf(start, StringComparison.Ordinal);
        var e = text.IndexOf(end, StringComparison.Ordinal);
        return s < 0 || e < s ? null : text[(s + start.Length)..e].Replace("\r\n", "\n").Trim('\n', ' ');
    }

    /// <summary>What happens to one block: it is created, updated (the text differs), removed (no longer wanted) or left alone.</summary>
    private static LocalPart Part(string id, string file, string? oldFile, string start, string end, string? wanted)
    {
        var had = Inner(oldFile, start, end);
        var action = wanted is null
            ? (had is null ? "unchanged" : "remove")
            : had is null ? (oldFile is null ? "create" : "update") : (had == wanted.Trim('\n') ? "unchanged" : "update");
        return new LocalPart(id, file, action, wanted);
    }

    private static void Write(string path, string text)
    {
        var tmp = path + ".radar-tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

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

public sealed class LocalFileFailure : Exception;
