import { describe, expect, it } from 'vitest';
import { COVERAGE_HIGH, COVERAGE_MID, COVERAGE_PARTS, COVERAGE_WEIGHTS } from './coverage';
import { COLOR, coverageColor } from './palette';

describe('coverage legend constants', () => {
  it('the weights add up to 100', () => {
    expect(COVERAGE_PARTS.reduce((sum, p) => sum + COVERAGE_WEIGHTS[p], 0)).toBe(100);
  });

  it('match the weights in the scanner output (sample data)', async () => {
    const sample = await import('../../../public/mock/scan-result.json');
    for (const r of sample.repos) {
      for (const p of COVERAGE_PARTS) expect([0, COVERAGE_WEIGHTS[p]]).toContain(r.coverage.parts[p]);
    }
  });

  it('the colour thresholds are the ones the orbit uses', () => {
    expect(coverageColor(COVERAGE_HIGH)).toBe('#ffffff');
    expect(coverageColor(COVERAGE_HIGH - 1)).toBe(COLOR.amber);
    expect(coverageColor(COVERAGE_MID)).toBe(COLOR.amber);
    expect(coverageColor(COVERAGE_MID - 1)).toBe(COLOR.magenta);
  });
});
