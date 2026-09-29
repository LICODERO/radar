import { describe, expect, it } from 'vitest';
import { FONT_MAX, FONT_MIN, MIN_SCALE, computeFit } from './fit-scale';

describe('computeFit', () => {
  it('is identity at the base size', () => {
    const f = computeFit(1440, 900);
    expect(f.scale).toBe(1);
    expect(f.fs).toBe(1);
    expect(f.left).toBe(0);
    expect(f.top).toBe(0);
  });

  it('scales proportionally and letterboxes the stage', () => {
    const f = computeFit(2880, 1200);
    expect(f.scale).toBeCloseTo(1200 / 900);
    expect(f.left).toBeCloseTo((2880 - 1440 * f.scale) / 2);
    expect(f.top).toBe(0);
  });

  it('keeps effective font size within 0.85x-1.2x', () => {
    for (const [w, h] of [[1440, 900], [1728, 1080], [2880, 1800], [3840, 2160]]) {
      const f = computeFit(w, h);
      const effective = f.scale * f.fs;
      expect(effective).toBeGreaterThanOrEqual(FONT_MIN - 1e-9);
      expect(effective).toBeLessThanOrEqual(FONT_MAX + 1e-9);
    }
  });

  it('never goes below the minimum scale', () => {
    expect(computeFit(200, 100).scale).toBe(MIN_SCALE);
  });

  it('falls back to identity for an invalid viewport', () => {
    expect(computeFit(0, 0)).toEqual({ scale: 1, fs: 1, left: 0, top: 0 });
  });
});
