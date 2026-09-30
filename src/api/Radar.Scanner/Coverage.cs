namespace Radar.Scanner;

public static class Coverage
{
    public const int ClaudeMdWeight = 40;
    public const int AgentsWeight = 25;
    public const int SkillsWeight = 25;
    public const int OutputsWeight = 10;

    /// <summary>Coverage = CLAUDE.md 40 + agents(>=1) 25 + skills(>=1) 25 + OUTPUTS 10.</summary>
    public static CoverageInfo Compute(bool claudeMd, int agents, int skills, bool outputs)
    {
        var parts = new CoverageParts(
            claudeMd ? ClaudeMdWeight : 0,
            agents > 0 ? AgentsWeight : 0,
            skills > 0 ? SkillsWeight : 0,
            outputs ? OutputsWeight : 0);
        return new CoverageInfo(parts.ClaudeMd + parts.Agents + parts.Skills + parts.Outputs, parts);
    }
}
