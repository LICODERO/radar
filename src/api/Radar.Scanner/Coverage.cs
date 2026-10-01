namespace Radar.Scanner;

public static class Coverage
{
    public const int ClaudeMdWeight = 40;
    public const int AgentsWeight = 30;
    public const int SkillsWeight = 30;

    /// <summary>Coverage = CLAUDE.md 40 + agents(>=1) 30 + skills(>=1) 30.</summary>
    public static CoverageInfo Compute(bool claudeMd, int agents, int skills)
    {
        var parts = new CoverageParts(
            claudeMd ? ClaudeMdWeight : 0,
            agents > 0 ? AgentsWeight : 0,
            skills > 0 ? SkillsWeight : 0);
        return new CoverageInfo(parts.ClaudeMd + parts.Agents + parts.Skills, parts);
    }
}
