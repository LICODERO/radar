using System.Diagnostics;

namespace Radar.Scanner;

/// <summary>Scans a directory of repositories (read-only) and reports real progress through <see cref="IProgress{T}"/>.</summary>
public sealed class RadarScanner
{
    public ScanResult Scan(ScanOptions options, IProgress<ScanEvent>? progress = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Root));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Katalog nie istnieje: {root}");

        progress?.Report(new ScanStarted(root));
        progress?.Report(new ScanPhase("init", "INICJALIZACJA", 0));

        // Phase 1: discovery (total unknown, so no percentage)
        progress?.Report(new ScanPhase("discovery", "SKANOWANIE KATALOGU", null));
        var dirs = new List<string>();
        foreach (var dir in RepoDiscovery.Discover(root, options.MaxDepth, ct))
        {
            dirs.Add(dir);
            progress?.Report(new RepoFound(dirs.Count, Path.GetFileName(dir)));
        }

        // Phase 2: analysis (total known, real percentage)
        progress?.Report(new ScanPhase("analysis", "ANALIZA AGENTÓW I SKILLI", 0));
        var repos = new List<RepoInfo>();
        var raw = new List<RawWorkflow>();
        var warnings = new List<WarningInfo>();
        int agents = 0, skills = 0, gaps = 0;
        for (var i = 0; i < dirs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var a = RepoAnalyzer.Analyze(root, dirs[i], ct);
            repos.Add(a.Repo);
            raw.AddRange(a.Workflows);
            warnings.AddRange(a.Warnings);
            agents += a.Repo.Agents.Count;
            skills += a.Repo.Skills.Count;
            if (!a.Repo.ClaudeMd.Exists) gaps++;
            progress?.Report(new RepoScanned(
                i + 1, dirs.Count, a.Repo.Id, a.Repo.Name, a.Repo.ClaudeMd.Exists, a.Repo.Agents.Count, a.Repo.Skills.Count,
                a.Repo.Coverage.Score, (int)Math.Round(90.0 * (i + 1) / dirs.Count),
                new ScanCounters(i + 1, agents, skills, gaps)));
        }

        // Phase 3: linking
        ct.ThrowIfCancellationRequested();
        var ordered = repos.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToList();
        var workflows = WorkflowAggregator.Aggregate(ordered, raw);
        progress?.Report(new ScanPhase("linking", "BUDOWANIE POWIĄZAŃ", 95, workflows.Count));

        var gapList = ordered.Where(r => !r.ClaudeMd.Exists).Select(r => new GapInfo(r.Id, GapTypes.NoClaudeMd)).ToList();
        var stacks = ordered.GroupBy(r => r.Stack).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).ToList();
        var summary = new SummaryInfo(
            ordered.Count, agents, skills, workflows.Count, gapList.Count, stacks,
            ordered.Count == 0 ? 0 : (int)Math.Round(ordered.Average(r => r.Coverage.Score)));

        sw.Stop();
        var result = new ScanResult(
            ScanResult.CurrentSchemaVersion, root, DateTimeOffset.Now, sw.ElapsedMilliseconds, summary,
            ordered, workflows, gapList, warnings);

        progress?.Report(new ScanPhase("done", "ZAKOŃCZONO", 100, workflows.Count));
        progress?.Report(new ScanCompleted(sw.ElapsedMilliseconds, summary));
        return result;
    }
}
