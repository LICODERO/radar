import { describe, expect, it } from 'vitest';
import { CENTER, R1, chooseSpread, lineBetween, polar, ringLayout } from './geometry';

describe('polar', () => {
  it('places angle 0 to the right of the center and -90 above it', () => {
    const [x, y] = polar(100, 0);
    expect(x).toBeCloseTo(CENTER + 100);
    expect(y).toBeCloseTo(CENTER);
    const [x2, y2] = polar(100, -90);
    expect(x2).toBeCloseTo(CENTER);
    expect(y2).toBeCloseTo(CENTER - 100);
  });
});

describe('lineBetween', () => {
  it('returns length and angle of the segment', () => {
    const l = lineBetween([0, 0], [3, 4]);
    expect(l.len).toBeCloseTo(5);
    expect(l.ang).toBeCloseTo(53.13, 1);
    expect(l.left).toBe(0);
    expect(l.top).toBe(0);
  });
});

describe('ringLayout', () => {
  it('keeps the mockup layout for 24 repos', () => {
    const l = ringLayout(24, R1, -90, 28);
    expect(l.tracks).toBe(1);
    expect(l.size).toBe(28);
    expect(l.labels).toBe(true);
    expect(l.step).toBeCloseTo(15);
    expect(l.angleOf(0)).toBe(-90);
    expect(l.radiusOf(5)).toBe(R1);
  });

  it('shrinks nodes and adds tracks as the ring fills up', () => {
    const a = ringLayout(60, R1, -90, 28);
    const b = ringLayout(150, R1, -90, 28);
    const c = ringLayout(400, R1, -90, 28);
    expect(a.size).toBeLessThan(28);
    expect(b.tracks).toBeGreaterThan(1);
    expect(c.tracks).toBeGreaterThanOrEqual(b.tracks);
    expect(c.size).toBeLessThanOrEqual(b.size);
  });

  it('never returns a node smaller than the minimum and hides labels when tiny', () => {
    const l = ringLayout(5000, R1, -90, 28, 5, 4);
    expect(l.size).toBeGreaterThanOrEqual(5);
    expect(l.labels).toBe(false);
  });

  it('alternates radii between tracks around the base radius', () => {
    const l = ringLayout(150, R1, -90, 28);
    const radii = new Set(Array.from({ length: l.tracks }, (_, i) => l.radiusOf(i)));
    expect(radii.size).toBe(l.tracks);
    const mean = [...radii].reduce((a, b) => a + b, 0) / radii.size;
    expect(mean).toBeCloseTo(R1);
  });

  it('handles an empty ring', () => {
    expect(() => ringLayout(0, R1, -90, 28)).not.toThrow();
  });
});

describe('chooseSpread', () => {
  it('spreads along the ring when there is room', () => {
    expect(chooseSpread(3, 15, 5, 3)).toEqual({ mode: 'arc', spacing: 5 });
  });

  it('shrinks spacing to fit the neighbours', () => {
    const s = chooseSpread(6, 15, 5, 1.6);
    expect(s.mode).toBe('arc');
    expect(s.spacing).toBeCloseTo(2.4);
  });

  it('falls back to a radial stack when it would overlap', () => {
    expect(chooseSpread(6, 2.4, 2.3, 1.6).mode).toBe('radial');
  });

  it('does not need spacing for a single element', () => {
    expect(chooseSpread(1, 1, 5, 3)).toEqual({ mode: 'arc', spacing: 0 });
  });
});
