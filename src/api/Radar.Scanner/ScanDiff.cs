namespace Radar.Scanner;

public sealed record RepoChange(string RepoId, string Name, int CoverageBefore, int CoverageAfter, int? QualityBefore, int? QualityAfter, int AgentsDelta, int SkillsDelta);

public sealed record FindingChange(string RepoId, string RepoName, string Path, string Code, string Severity, string? Detail);

/// <summary>What moved between two scans of the same directory.</summary>
public sealed record ScanChanges(
    DateTimeOffset PreviousScannedAt,
    DateTimeOffset ScannedAt,
    int AvgCoverageBefore,
    int AvgCoverageAfter,
    int? AvgQualityBefore,
    int? AvgQualityAfter,
    IReadOnlyList<string> NewRepos,
    IReadOnlyList<string> RemovedRepos,
    IReadOnlyList<RepoChange> Changed,
    IReadOnlyList<FindingChange> Fixed,
    IReadOnlyList<FindingChange> Introduced)
{
    public bool IsEmpty => NewRepos.Count == 0 && RemovedRepos.Count == 0 && Changed.Count == 0 && Fixed.Count == 0 && Introduced.Count == 0;
}

public static class ScanDiff
{
    /// <summary>Null when the scans are of different directories (nothing to compare).</summary>
    public static ScanChanges? Compute(ScanResult previous, ScanResult current)
    {
        if (!string.Equals(previous.ScanRoot, current.ScanRoot, StringComparison.Ordinal)) return null;

        var before = previous.Repos.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var after = current.Repos.ToDictionary(r => r.Id, StringComparer.Ordinal);

        var changed = new List<RepoChange>();
        var fixedF = new List<FindingChange>();
        var introduced = new List<FindingChange>();
        foreach (var (id, now) in after)
        {
            if (!before.TryGetValue(id, out var was)) continue;
            var agentsDelta = now.Agents.Count - was.Agents.Count;
            var skillsDelta = now.Skills.Count - was.Skills.Count;
            if (now.Coverage.Score != was.Coverage.Score || now.Quality?.Score != was.Quality?.Score || agentsDelta != 0 || skillsDelta != 0)
                changed.Add(new RepoChange(id, now.Name, was.Coverage.Score, now.Coverage.Score, was.Quality?.Score, now.Quality?.Score, agentsDelta, skillsDelta));

            // scans from before quality existed have nothing to compare, and must not report every finding as new
            if (was.Quality is null || now.Quality is null) continue;

            // a finding is the same finding while repo, file and code match (its detail, like a day count, may move)
            var wasKeys = was.Quality.Findings.ToDictionary(Key, f => f);
            var nowKeys = now.Quality.Findings.ToDictionary(Key, f => f);
            fixedF.AddRange(wasKeys.Where(kv => !nowKeys.ContainsKey(kv.Key)).Select(kv => Change(was, kv.Value)));
            introduced.AddRange(nowKeys.Where(kv => !wasKeys.ContainsKey(kv.Key)).Select(kv => Change(now, kv.Value)));
        }

        static string Key(QualityFinding f) => f.Path + "|" + f.Code;
        static FindingChange Change(RepoInfo r, QualityFinding f) => new(r.Id, r.Name, f.Path, f.Code, f.Severity, f.Detail);

        return new ScanChanges(
            previous.ScannedAt, current.ScannedAt,
            previous.Summary.AvgCoverage, current.Summary.AvgCoverage,
            previous.Summary.AvgQuality, current.Summary.AvgQuality,
            after.Keys.Where(id => !before.ContainsKey(id)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            before.Keys.Where(id => !after.ContainsKey(id)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            changed.OrderBy(c => c.RepoId, StringComparer.OrdinalIgnoreCase).ToList(),
            fixedF.OrderBy(f => f.RepoId, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Path, StringComparer.Ordinal).ToList(),
            introduced.OrderBy(f => f.RepoId, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Path, StringComparer.Ordinal).ToList());
    }
}
