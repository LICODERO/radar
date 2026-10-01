import { describe, expect, it } from 'vitest';
import { CHIPS_LIMIT, CHIPS_SHOWN, limitChips } from './chips';

const items = (n: number) => Array.from({ length: n }, (_, i) => 'i' + i);

describe('limitChips', () => {
  it('shows everything up to the limit', () => {
    expect(limitChips(items(0))).toEqual({ visible: [], extra: 0 });
    expect(limitChips(items(3))).toEqual({ visible: items(3), extra: 0 });
    expect(limitChips(items(CHIPS_LIMIT))).toEqual({ visible: items(CHIPS_LIMIT), extra: 0 });
  });

  it('collapses the rest into a +N tile beyond the limit', () => {
    const r = limitChips(items(CHIPS_LIMIT + 1));
    expect(r.visible).toEqual(items(CHIPS_SHOWN));
    expect(r.extra).toBe(CHIPS_LIMIT + 1 - CHIPS_SHOWN);
  });

  it('never leaves a tile that stands for a single item that would have fit', () => {
    for (let n = 0; n <= 40; n++) {
      const r = limitChips(items(n));
      expect(r.visible.length + r.extra).toBe(n);
      if (r.extra > 0) expect(r.extra).toBeGreaterThanOrEqual(2);
    }
  });
});
