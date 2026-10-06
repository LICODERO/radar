namespace Radar.Scanner;

public static class WorkflowAggregator
{
    /// <summary>Groups same-named workflows across repos into one node (W1, W2... ordered by name).</summary>
    public static IReadOnlyList<WorkflowInfo> Aggregate(IReadOnlyList<RepoInfo> repos, IEnumerable<RawWorkflow> raw)
    {
        var agentsByRepo = repos.ToDictionary(r => r.Id, r => r.Agents.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var skillsByRepo = repos.ToDictionary(r => r.Id, r => r.Skills.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));

        return raw
            .GroupBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select((g, i) =>
            {
                var items = g.OrderBy(w => w.RepoId, StringComparer.OrdinalIgnoreCase).ToList();
                var first = items.FirstOrDefault(w => w.Description.Length > 0) ?? items[0];
                var agents = items.FirstOrDefault(w => w.Agents.Count > 0)?.Agents ?? [];
                var skills = items.FirstOrDefault(w => w.Skills.Count > 0)?.Skills ?? [];
                var issues = items
                    .SelectMany(w => w.Agents
                        .Where(a => !agentsByRepo[w.RepoId].Contains(a))
                        .Select(a => $"{w.RepoId}: brak agenta '{a}'")
                        .Concat(w.Skills
                            .Where(s => !skillsByRepo[w.RepoId].Contains(s))
                            .Select(s => $"{w.RepoId}: brak skilla '{s}'")))
                    .ToList();
                return new WorkflowInfo(
                    $"W{i + 1}",
                    items[0].Name,
                    first.Description,
                    items.FirstOrDefault(w => w.When.Length > 0)?.When ?? string.Empty,
                    agents,
                    skills,
                    items.Select(w => new WorkflowRepoRef(w.RepoId, w.Path, w.Linked, w.Agents, w.Skills, w.Visibility)).ToList(),
                    issues);
            })
            .ToList();
    }
}
