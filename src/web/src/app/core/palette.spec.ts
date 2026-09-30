import { describe, expect, it } from 'vitest';
import { coverageColor } from './palette';

describe('coverageColor', () => {
  it('follows the mockup thresholds', () => {
    expect(coverageColor(100)).toBe('#ffffff');
    expect(coverageColor(70)).toBe('#ffffff');
    expect(coverageColor(69)).toBe('#ffb84d');
    expect(coverageColor(40)).toBe('#ffb84d');
    expect(coverageColor(39)).toBe('#ff4fa3');
    expect(coverageColor(0)).toBe('#ff4fa3');
  });
});
