namespace Radar.Scanner;

/// <summary>Finds agents and skills that live in more than one repo, so copies that drifted apart (or never got copied) show up.</summary>
public static class SharedItems
{
    public static IReadOnlyList<SharedItemInfo> Compute(IReadOnlyList<RepoInfo> repos)
    {
        var holders = new List<(string Kind, string Name, string RepoId, string Path, string Hash)>();
        foreach (var r in repos)
        {
            foreach (var a in r.Agents.Where(a => a.Hash is not null)) holders.Add((FileKinds.Agent, a.Name, r.Id, a.Path, a.Hash!));
            foreach (var s in r.Skills.Where(s => s.Hash is not null)) holders.Add((FileKinds.Skill, s.Name, r.Id, s.Path, s.Hash!));
        }
        var stackOf = repos.ToDictionary(r => r.Id, r => r.Stack);

        return holders
            .GroupBy(h => (h.Kind, Name: h.Name.ToLowerInvariant()))
            .Where(g => g.Select(h => h.RepoId).Distinct().Count() >= 2)
            .Select(g =>
            {
                var variants = g.GroupBy(h => h.Hash)
                    .Select(v =>
                    {
                        var repoIds = v.Select(h => h.RepoId).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                        // the path is the one in the first repo listed, so "open" lands on a file that exists there (file names can differ per repo)
                        return new ItemVariant(v.Key, repoIds, v.First(h => h.RepoId == repoIds[0]).Path);
                    })
                    .OrderByDescending(v => v.Repos.Count).ThenBy(v => v.Repos[0], StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var held = g.Select(h => h.RepoId).ToHashSet();
                var stacks = held.Select(id => stackOf[id]).ToHashSet();
                var missing = repos.Where(r => !held.Contains(r.Id) && stacks.Contains(r.Stack)).Select(r => r.Id).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                return new SharedItemInfo(g.Key.Kind, g.First().Name, variants, missing);
            })
            // drifted first (more variants), then the most widespread, then by name
            .OrderByDescending(i => i.Variants.Count > 1)
            .ThenByDescending(i => i.Variants.Sum(v => v.Repos.Count))
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
