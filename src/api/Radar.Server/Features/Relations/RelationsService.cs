using System.Text;
using Radar.Scanner;
using Radar.Server.Features.Gaps;
using Radar.Server.Features.LocalFile;
using Radar.Server.Features.Vault;
using Radar.Server.Features.Visibility;

namespace Radar.Server.Features.Relations;

/// <param name="Repo">repo name</param>
/// <param name="Parts">what changes in that repo (ids: exclude | relations | relations-local | pointer | pointer-local | agents-md)</param>
/// <param name="Notes">codes the UI turns into advice: no-claude-md</param>
public sealed record RepoRelationsPlan(string Repo, IReadOnlyList<LocalPart> Parts, IReadOnlyList<string> Notes);

/// <param name="Blocked">null, or why nothing is written: no-git | local-tracked | exclude-damaged | file-damaged | forbidden | too-big | ambiguous | unknown-repo</param>
/// <param name="BlockedRepo">the repo the block is about</param>
public sealed record RelationsPlan(IReadOnlyList<RepoRelationsPlan> Repos, string? Blocked, string? BlockedRepo, bool Applied);

public sealed record RelationsGraph(IReadOnlyList<Relation> Relations);

/// <summary>
/// Reads the flow rules back from the repos and writes them: a rule lives in the relations file of both of its repos, and each repo gets a
/// short pointer in CLAUDE.md (public rules) or CLAUDE.local.md (private rules), so an agent working in one repo knows about the other.
/// Every text is a marked block, so only RADAR's own lines are ever rewritten. Without <c>apply</c> nothing is written, and a failure
/// during the write restores every file that was already changed.
/// </summary>
public static class RelationsService
{
    private const int MaxBytes = 1024 * 1024;
    private const string LocalName = "CLAUDE.local.md";
    private const string ClaudeName = "CLAUDE.md";

    private sealed record Target(RepoInfo Repo, string Dir);

    /// <summary>All rules found in the repos' relations files, between repos of the scan only.</summary>
    public static RelationsGraph Load(ScanResult scan)
    {
        var names = UniqueNames(scan);
        var found = new Dictionary<string, Relation>();
        foreach (var repo in scan.Repos)
        {
            if (!names.Contains(repo.Name)) continue;
            var dir = SafeDir(scan, repo);
            if (dir is null) continue;
            foreach (var rel in new[] { RelationsText.PublicFile, RelationsText.PrivateFile })
            {
                var text = ReadSmall(dir, rel);
                foreach (var r in RelationsText.Read(text))
                {
                    // a file only speaks for its own visibility, so a private rule cannot be smuggled into the public file
                    var expected = rel == RelationsText.PublicFile ? Relation.Public : Relation.Private;
                    if (r.Visibility != expected || !names.Contains(r.From) || !names.Contains(r.To)) continue;
                    found.TryAdd(r.Key, r);
                }
            }
        }
        return new RelationsGraph(found.Values.OrderBy(r => r.From, StringComparer.Ordinal).ThenBy(r => r.To, StringComparer.Ordinal).ThenBy(r => r.Kind, StringComparer.Ordinal).ToList());
    }

    /// <summary>Plans (and with <paramref name="apply"/> writes) the whole set of rules <paramref name="desired"/>; repos that lose their last rule are cleaned up.</summary>
    public static RelationsPlan Run(ScanResult scan, IReadOnlyList<Relation> desired, bool apply)
    {
        RelationsPlan Blocked(string why, string? repo) => new([], why, repo, false);

        var names = UniqueNames(scan);
        var byName = scan.Repos.Where(r => names.Contains(r.Name)).ToDictionary(r => r.Name);
        foreach (var r in desired)
            foreach (var n in new[] { r.From, r.To })
                if (!byName.ContainsKey(n)) return Blocked(scan.Repos.Count(x => x.Name == n) > 1 ? "ambiguous" : "unknown-repo", n);

        var before = Load(scan).Relations;
        var touched = desired.SelectMany(r => new[] { r.From, r.To })
            .Concat(before.SelectMany(r => new[] { r.From, r.To }))
            .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();

        var steps = new List<Step>();
        var plans = new List<RepoRelationsPlan>();
        foreach (var name in touched)
        {
            var repo = byName[name];
            var dir = SafeDir(scan, repo);
            if (dir is null) return Blocked("forbidden", name);
            var step = Plan(new Target(repo, dir), desired.Where(r => r.Involves(name)).ToList(), out var why);
            if (step is null) return Blocked(why!, name);
            steps.Add(step);
            if (step.Parts.Count > 0) plans.Add(new RepoRelationsPlan(name, step.Parts, step.Notes));
        }

        var plan = new RelationsPlan(plans, null, null, false);
        if (!apply || plans.Count == 0) return plan;

        var undo = new List<Action>();
        try
        {
            foreach (var step in steps) step.Write(undo);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or VisibilityFailure)
        {
            for (var i = undo.Count - 1; i >= 0; i--) undo[i]();
            throw new RelationsFailure();
        }
        return plan with { Applied = true };
    }

    /// <summary>What happens in one repo: the new text of each file (null text = file untouched) and the exclude entries needed first.</summary>
    private sealed class Step(Target target)
    {
        public List<LocalPart> Parts { get; } = [];
        public List<string> Notes { get; } = [];
        public List<(string Path, string? Old, string? New)> Files { get; } = [];
        public List<string> Hide { get; } = [];

        public void Write(List<Action> undo)
        {
            // private files are put on the exclude list first, so they are never exposed
            if (Hide.Count > 0)
            {
                var file = PrivateFileGuard.ExcludeFile(target.Dir);
                var old = file is not null && System.IO.File.Exists(file) ? System.IO.File.ReadAllText(file) : null;
                foreach (var h in Hide) PrivateFileGuard.Apply(target.Dir, h);
                if (file is not null) undo.Add(() => Restore(file, old));
            }
            foreach (var (path, old, text) in Files)
            {
                if (text is null) { if (old is not null) System.IO.File.Delete(path); }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var tmp = path + ".radar-tmp";
                    System.IO.File.WriteAllText(tmp, text, new UTF8Encoding(false));
                    System.IO.File.Move(tmp, path, overwrite: true);
                }
                undo.Add(() => Restore(path, old));
            }
        }
    }

    private static Step? Plan(Target t, IReadOnlyList<Relation> mine, out string? why)
    {
        why = null;
        var name = t.Repo.Name;
        var step = new Step(t);
        var claudeDir = Path.Combine(t.Dir, ".claude");
        if (File.Exists(claudeDir) || (Directory.Exists(claudeDir) && !SafeFs.IsInside(t.Dir, claudeDir))) { why = "forbidden"; return null; }

        var publicRules = mine.Where(r => r.Visibility == Relation.Public).ToList();
        var privateRules = mine.Where(r => r.Visibility == Relation.Private).ToList();

        var pubFile = FileFor(t.Dir, RelationsText.PublicFile, out why);
        var privFile = why is null ? FileFor(t.Dir, RelationsText.PrivateFile, out why) : null;
        var claude = why is null ? FileFor(t.Dir, ClaudeName, out why) : null;
        var local = why is null ? FileFor(t.Dir, LocalName, out why) : null;
        if (why is not null) return null;

        // the relations files
        var pubNew = Block(pubFile!.Text, RelationsText.FileStart, RelationsText.FileEnd, publicRules.Count > 0 ? RelationsText.File(name, publicRules) : null, out var bad1);
        var privNew = Block(privFile!.Text, RelationsText.FileStart, RelationsText.FileEnd, privateRules.Count > 0 ? RelationsText.File(name, privateRules) : null, out var bad2);
        if (bad1 || bad2) { why = "file-damaged"; return null; }

        // the pointers
        string? pubPointer = publicRules.Count > 0 ? RelationsText.Pointer(name, publicRules, RelationsText.PublicFile) : null;
        string? privPointer = privateRules.Count > 0 ? RelationsText.Pointer(name, privateRules, RelationsText.PrivateFile) : null;
        string? claudeNew = claude!.Text;
        if (claude.Text is null && pubPointer is not null) step.Notes.Add("no-claude-md");
        else
        {
            claudeNew = Block(claude.Text, RelationsText.RefStart, RelationsText.RefEnd, pubPointer, out var bad3);
            if (bad3) { why = "file-damaged"; return null; }
        }
        var localNew = Block(local!.Text, RelationsText.RefStart, RelationsText.RefEnd, privPointer, out var bad4);
        if (bad4) { why = "file-damaged"; return null; }
        // a CLAUDE.local.md that Claude finds first would silence AGENTS.md, so keep it imported like the local-instructions flow does
        if (privPointer is not null && LocalBlocks.NeedsAgentsImport(t.Dir) && !(localNew?.Contains(LocalBlocks.AgentsStart, StringComparison.Ordinal) ?? false))
            localNew = Block(localNew, LocalBlocks.AgentsStart, LocalBlocks.AgentsEnd, LocalBlocks.AgentsImport, out _);

        // private files must never reach a commit
        foreach (var (rel, oldText, newText) in new[] { (RelationsText.PrivateFile, privFile.Text, privNew), (LocalName, local.Text, localNew) })
        {
            if (newText == oldText || string.IsNullOrWhiteSpace(newText)) continue;
            var guard = PrivateFileGuard.Check(t.Dir, rel);
            if (guard.Blocked is not null) { why = guard.Blocked == "tracked" ? "local-tracked" : guard.Blocked; return null; }
            if (guard.ExcludeEntry is not null)
            {
                step.Hide.Add(rel);
                step.Parts.Add(new LocalPart("exclude", ".git/info/exclude", "update", guard.ExcludeEntry));
            }
        }

        Add(step, "relations", RelationsText.PublicFile, pubFile, pubNew, publicRules.Count > 0 ? RelationsText.File(name, publicRules) : null);
        Add(step, "relations-local", RelationsText.PrivateFile, privFile, privNew, privateRules.Count > 0 ? RelationsText.File(name, privateRules) : null);
        Add(step, "pointer", ClaudeName, claude, claudeNew, pubPointer);
        Add(step, "pointer-local", LocalName, local, localNew, privPointer);
        return step;

        //string? s) => s;
    }

    private sealed record Loaded(string Path, string? Text);

    private static Loaded? FileFor(string dir, string rel, out string? why)
    {
        why = null;
        var path = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(path) || !SafeFs.IsInside(dir, path)) { why = "forbidden"; return null; }
        if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) { why = "too-big"; return null; }
        return new Loaded(path, File.Exists(path) ? File.ReadAllText(path) : null);
    }

    /// <summary>The text with the block set (or removed when <paramref name="inner"/> is null); a file that only held the block ends up empty (null).</summary>
    private static string? Block(string? text, string start, string end, string? inner, out bool damaged)
    {
        damaged = false;
        var result = inner is not null ? MarkedBlock.Upsert(text, start, end, inner) : MarkedBlock.Remove(text, start, end);
        if (result is null && (inner is not null || text is not null)) { damaged = true; return text; }
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static void Add(Step step, string id, string rel, Loaded file, string? newText, string? wanted)
    {
        if (newText == file.Text) return;
        var action = newText is null ? "remove" : file.Text is null ? "create" : "update";
        step.Parts.Add(new LocalPart(id, rel, action, wanted));
        step.Files.Add((file.Path, file.Text, newText));
    }

    private static HashSet<string> UniqueNames(ScanResult scan) =>
        scan.Repos.GroupBy(r => r.Name).Where(g => g.Count() == 1).Select(g => g.Key).ToHashSet();

    private static string? SafeDir(ScanResult scan, RepoInfo repo)
    {
        var dir = GapCommands.DirOf(scan, repo);
        return Directory.Exists(dir) && SafeFs.IsInside(Path.GetFullPath(scan.ScanRoot), dir) ? dir : null;
    }

    private static string? ReadSmall(string dir, string rel)
    {
        var path = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            if (!File.Exists(path) || !SafeFs.IsInside(dir, path) || new FileInfo(path).Length > SafeFs.MaxFileBytes) return null;
            return File.ReadAllText(path);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
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

public sealed class RelationsFailure : Exception;
