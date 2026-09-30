/** Points a repo earns for each part of its AI setup; must match `Radar.Scanner/Coverage.cs` (sums to 100). */
export const COVERAGE_WEIGHTS = { claudeMd: 40, agents: 25, skills: 25, outputs: 10 } as const;

export type CoveragePart = keyof typeof COVERAGE_WEIGHTS;
export const COVERAGE_PARTS: readonly CoveragePart[] = ['claudeMd', 'agents', 'skills', 'outputs'];

/** Repo colour thresholds (see `coverageColor`): white from this score up, amber from the next, magenta below. */
export const COVERAGE_HIGH = 70;
export const COVERAGE_MID = 40;
