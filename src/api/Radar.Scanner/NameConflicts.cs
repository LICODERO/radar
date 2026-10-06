namespace Radar.Scanner;

public sealed record ConflictItem(string Path, string Visibility);

/// <summary>Two or more agents, skills or workflows of one repo that share a name while one of them is shared with the team and another is only local.</summary>
/// <param name="Kind">agent | skill | workflow</param>
public sealed record NameConflict(string Kind, string Name, IReadOnlyList<ConflictItem> Items);

/// <summary>
/// A private item with the name of a public one (or the other way round): Claude sees both and may pick the wrong one, and the day the
/// public file reaches the private one's path, git refuses to pull. Items of unknown visibility are ignored, and so are same-visibility
/// duplicates (agents have their own quality finding for those).
/// </summary>
public static class NameConflicts
{
    public static IReadOnlyList<NameConflict> Compute(IReadOnlyList<AgentInfo> agents, IReadOnlyList<SkillInfo> skills, IReadOnlyList<RawWorkflow> workflows)
    {
        var all = agents.Select(a => ("agent", a.Name, a.Path, a.Visibility))
            .Concat(skills.Select(s => ("skill", s.Name, s.Path, s.Visibility)))
            .Concat(workflows.Select(w => ("workflow", w.Name, w.Path, w.Visibility)));

        return all
            .GroupBy(i => (i.Item1, Name: i.Item2.ToLowerInvariant()))
            .Select(g => g.ToList())
            .Where(g => g.Count > 1
                && g.Any(i => i.Item4 == Visibilities.Public)
                && g.Any(i => i.Item4 is Visibilities.Private or Visibilities.Untracked))
            .Select(g => new NameConflict(g[0].Item1, g[0].Item2, g
                .Where(i => i.Item4 is Visibilities.Public or Visibilities.Private or Visibilities.Untracked)
                .OrderBy(i => i.Item4 == Visibilities.Public ? 0 : 1).ThenBy(i => i.Item3, StringComparer.Ordinal)
                .Select(i => new ConflictItem(i.Item3, i.Item4!)).ToList()))
            .OrderBy(c => c.Kind, StringComparer.Ordinal).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
