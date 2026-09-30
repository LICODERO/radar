import { describe, expect, it } from 'vitest';
import { buildPager, clampPage, pageCount } from './pager';

describe('pager', () => {
  it('counts pages', () => {
    expect(pageCount(24, 10)).toBe(3);
    expect(pageCount(9, 3)).toBe(3);
    expect(pageCount(0, 10)).toBe(1);
  });

  it('labels the first and the last page', () => {
    expect(buildPager(24, 10, 0).label).toBe('1–10 z 24');
    expect(buildPager(24, 10, 2).label).toBe('21–24 z 24');
    expect(buildPager(9, 3, 1).label).toBe('4–6 z 9');
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
    expect(buildPager(0, 10, 0).label).toBe('0–0 z 0');
  });
});
