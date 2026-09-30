import { describe, expect, it } from 'vitest';
import { buildPager, clampPage, pageCount } from './pager';

describe('pager', () => {
  it('counts pages', () => {
    expect(pageCount(24, 10)).toBe(3);
    expect(pageCount(9, 3)).toBe(3);
    expect(pageCount(0, 10)).toBe(1);
  });

  it('labels the first and the last page', () => {
    expect(buildPager(24, 10, 0)).toMatchObject({ from: 1, to: 10, total: 24 });
    expect(buildPager(24, 10, 2)).toMatchObject({ from: 21, to: 24, total: 24 });
    expect(buildPager(9, 3, 1)).toMatchObject({ from: 4, to: 6, total: 9 });
  });

  it('disables prev/next at the edges', () => {
    const first = buildPager(24, 10, 0);
    const last = buildPager(24, 10, 2);
    expect(first.prevDisabled).toBe(true);
    expect(first.nextDisabled).toBe(false);
    expect(last.nextDisabled).toBe(true);
  });

  it('clamps out-of-range pages and handles empty lists', () => {
    expect(clampPage(99, 24, 10)).toBe(2);
    expect(clampPage(-3, 24, 10)).toBe(0);
    expect(buildPager(0, 10, 0)).toMatchObject({ from: 0, to: 0, total: 0 });
  });
});
