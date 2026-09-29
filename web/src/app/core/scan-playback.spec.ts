import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MAX_DELAY_MS, MIN_DELAY_MS, ScanPlayback, TARGET_MS, paceDelay } from './scan-playback';

describe('paceDelay', () => {
  it('aims for about TARGET_MS in total', () => {
    expect(paceDelay(25) * 25).toBeCloseTo(TARGET_MS, -2);
  });

  it('is bounded for tiny and huge scans', () => {
    expect(paceDelay(1)).toBe(MAX_DELAY_MS);
    expect(paceDelay(5000)).toBe(MIN_DELAY_MS);
    expect(paceDelay(null)).toBe(MAX_DELAY_MS);
  });
});

describe('ScanPlayback', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  const scanned = (i: number, total = 3) => ({ index: i, total });

  it('shows non-paced events immediately and in order', () => {
    const seen: string[] = [];
    const p = new ScanPlayback((n) => seen.push(n));
    p.push('started', {});
    p.push('phase', {});
    p.push('repo-found', {});
    expect(seen).toEqual(['started', 'phase', 'repo-found']);
  });

  it('spaces repo-scanned events and keeps completed behind them', () => {
    const seen: string[] = [];
    const p = new ScanPlayback((n, d) => seen.push(n === 'repo-scanned' ? 'r' + d.index : n));
    ['1', '2', '3'].forEach((_, i) => p.push('repo-scanned', scanned(i + 1)));
    p.push('completed', {});

    expect(seen).toEqual(['r1']);
    vi.advanceTimersByTime(paceDelay(3) - 1);
    expect(seen).toEqual(['r1']);
    vi.advanceTimersByTime(1);
    expect(seen).toEqual(['r1', 'r2']);
    vi.advanceTimersByTime(paceDelay(3));
    expect(seen).toEqual(['r1', 'r2', 'r3']);          // completed waits for the last repo to be visible
    vi.advanceTimersByTime(paceDelay(3));
    expect(seen).toEqual(['r1', 'r2', 'r3', 'completed']);
    expect(p.pending).toBe(0);
  });

  it('flush shows everything at once', () => {
    const seen: string[] = [];
    const p = new ScanPlayback((n) => seen.push(n));
    p.push('repo-scanned', scanned(1));
    p.push('repo-scanned', scanned(2));
    p.push('completed', {});
    p.flush();
    expect(seen).toEqual(['repo-scanned', 'repo-scanned', 'completed']);
    vi.advanceTimersByTime(10_000);
    expect(seen).toHaveLength(3);
  });

  it('clear drops what is still waiting', () => {
    const seen: string[] = [];
    const p = new ScanPlayback((n) => seen.push(n));
    p.push('repo-scanned', scanned(1));
    p.push('repo-scanned', scanned(2));
    p.clear();
    vi.advanceTimersByTime(10_000);
    expect(seen).toEqual(['repo-scanned']);
  });

  it('continues after the queue ran dry', () => {
    const seen: string[] = [];
    const p = new ScanPlayback((n) => seen.push(n));
    p.push('repo-scanned', scanned(1));
    vi.advanceTimersByTime(1000);
    p.push('repo-scanned', scanned(2));
    expect(seen).toHaveLength(2);
  });
});
