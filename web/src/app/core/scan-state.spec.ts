import { describe, expect, it } from 'vitest';
import { LOG_LINES, applyScanEvent, blipPosition, initialScan } from './scan-state';

const scanned = (i: number, total = 30, o: Record<string, unknown> = {}) => ({
  index: i, total, id: 'repo' + i, name: 'repo' + i, hasClaudeMd: i % 2 === 0, agents: 1, skills: 2, coverage: 40, percent: 30,
  counters: { repos: i, agents: i, skills: 2 * i, gaps: 0 }, ...o
});

describe('applyScanEvent', () => {
  it('starts from a clean running state', () => {
    const s = applyScanEvent(initialScan(), 'started', { scanRoot: '/x' });
    expect(s.status).toBe('running');
    expect(s.percent).toBe(0);
    expect(s.total).toBeNull();
  });

  it('keeps the percentage unknown during the directory walk', () => {
    let s = applyScanEvent(initialScan(), 'phase', { phase: 'discovery', label: 'SKANOWANIE KATALOGU', percent: null });
    expect(s.percent).toBeNull();
    s = applyScanEvent(s, 'repo-found', { found: 7, name: 'a' });
    expect(s.found).toBe(7);
    expect(s.percent).toBeNull();
  });

  it('takes counters, percent and total from repo-scanned events', () => {
    const s = applyScanEvent(initialScan(), 'repo-scanned', scanned(3));
    expect(s.total).toBe(30);
    expect(s.percent).toBe(30);
    expect(s.counters.repos).toBe(3);
    expect(s.blips).toEqual([{ index: 2, coverage: 40 }]);
  });

  it('keeps only the last log lines but every blip', () => {
    let s = initialScan();
    for (let i = 1; i <= 25; i++) s = applyScanEvent(s, 'repo-scanned', scanned(i));
    expect(s.log).toHaveLength(LOG_LINES);
    expect(s.log[LOG_LINES - 1].name).toBe('repo25');
    expect(s.blips).toHaveLength(25);
  });

  it('marks completion, cancellation and errors', () => {
    const done = applyScanEvent(initialScan(), 'completed', { durationMs: 1200, summary: { workflows: 4 } });
    expect(done.status).toBe('done');
    expect(done.percent).toBe(100);
    expect(done.workflows).toBe(4);
    expect(done.durationMs).toBe(1200);
    expect(applyScanEvent(initialScan(), 'cancelled', {}).status).toBe('cancelled');
    const err = applyScanEvent(initialScan(), 'error', { message: 'Boom' });
    expect(err.status).toBe('error');
    expect(err.message).toBe('Boom');
  });

  it('ignores unknown events', () => {
    const s = initialScan();
    expect(applyScanEvent(s, 'whatever', {})).toBe(s);
  });
});

describe('blipPosition', () => {
  it('places the first blip straight above the center', () => {
    const p = blipPosition(0, 4, 0);
    expect(p.left).toBeCloseTo(220 - 6, 0);
    expect(p.top).toBeLessThan(220);
  });

  it('moves blips towards the middle as coverage grows', () => {
    const low = blipPosition(0, 4, 10);
    const high = blipPosition(0, 4, 90);
    expect(220 - high.top).toBeLessThan(220 - low.top);
  });
});
