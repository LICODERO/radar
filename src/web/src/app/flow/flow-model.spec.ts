import { describe, expect, it } from 'vitest';
import { FlowRule } from '../core/models';
import { CARD_H, CARD_W, MAX_ZOOM, MIN_ZOOM, WORLD_MAX, WORLD_MIN, away, clampPos, curve, facing, portPos, fitView, freeKind, freeSpot, newRule, parseEnv, ruleKey, sameRules, zoomAt } from './flow-model';

describe('flow-model', () => {
  it('splits environment variable names and flags what is not a name', () => {
    expect(parseEnv('API_ID, API_SECRET;API_ID')).toEqual({ names: ['API_ID', 'API_SECRET'], bad: [] });
    expect(parseEnv('sk_live=abc123 OK_NAME').bad).toEqual(['sk_live=abc123']);
    expect(parseEnv('').names).toEqual([]);
  });

  it('gives one rule per from, to and kind, and offers the next free kind', () => {
    const a = newRule('web', 'api');
    expect(ruleKey(a)).not.toBe(ruleKey(newRule('web', 'api', 'events')));
    expect(freeKind([a], 'web', 'api')).toBe('events');
    expect(freeKind([], 'api', 'web')).toBe('rest');
    const all = ['rest', 'events', 'graphql', 'grpc', 'db', 'files', 'other'].map((k) => newRule('web', 'api', k as FlowRule['kind']));
    expect(freeKind(all, 'web', 'api')).toBeNull();
  });

  it('compares sets of rules whatever the order and the order of variable names', () => {
    const a = { ...newRule('web', 'api'), auth: 'api-key' as const, authEnv: ['A', 'B'] };
    const b = newRule('api', 'db', 'db');
    expect(sameRules([a, b], [b, { ...a, authEnv: ['B', 'A'] }])).toBe(true);
    expect(sameRules([a], [{ ...a, notes: 'x' }])).toBe(false);
    expect(sameRules([a], [a, b])).toBe(false);
  });

  it('puts a dot in the middle of each side of a card', () => {
    const c = { x: 100, y: 50 };
    expect(portPos(c, 'top')).toEqual({ x: 100 + CARD_W / 2, y: 50 });
    expect(portPos(c, 'bottom')).toEqual({ x: 100 + CARD_W / 2, y: 50 + CARD_H });
    expect(portPos(c, 'left')).toEqual({ x: 100, y: 50 + CARD_H / 2 });
    expect(portPos(c, 'right')).toEqual({ x: 100 + CARD_W, y: 50 + CARD_H / 2 });
    expect(away({ x: 10, y: 10 }, 'top', 9)).toEqual({ x: 10, y: 1 });
    expect(away({ x: 10, y: 10 }, 'right', 9)).toEqual({ x: 19, y: 10 });
  });

  it('faces the side that looks towards the other card', () => {
    const o = { x: 0, y: 0 };
    expect(facing(o, { x: 500, y: 20 })).toBe('right');
    expect(facing(o, { x: -500, y: 20 })).toBe('left');
    expect(facing(o, { x: 20, y: 400 })).toBe('bottom');
    expect(facing(o, { x: 20, y: -400 })).toBe('top');
  });

  it('draws a curve that starts and ends at the dots and leaves along the side it starts on', () => {
    const c = curve({ x: 0, y: 0 }, 'bottom', { x: 300, y: 200 }, 'top');
    expect(c.d).toMatch(/^M 0 0 C 0 [\d.]+, 300 [\d.]+, 300 200$/);
    expect(c.mid.x).toBeCloseTo(150);
    expect(c.mid.y).toBeCloseTo(100);
    expect(curve({ x: 0, y: 0 }, 'right', { x: 50, y: 50 }, null).d).toMatch(/50 50$/);
  });

  it('lets cards go anywhere in a large world and finds a spot no card covers', () => {
    expect(clampPos({ x: -50, y: -120 })).toEqual({ x: -50, y: -120 });          // above and left of the origin is fine
    expect(clampPos({ x: -99999, y: 99999 })).toEqual({ x: WORLD_MIN, y: WORLD_MAX - CARD_H });
    expect(clampPos({ x: 99999, y: 3 }).x).toBe(WORLD_MAX - CARD_W);
    const first = freeSpot([]);
    const second = freeSpot([first]);
    expect(second).not.toEqual(first);
    expect(Math.abs(second.x - first.x) >= CARD_W || Math.abs(second.y - first.y) >= CARD_H).toBe(true);
  });

  it('zooms around the pointer so the point under it stays put, within the limits', () => {
    const v = zoomAt({ x: 10, y: 20, k: 1 }, 1.5, 300, 200);
    expect(v.k).toBe(1.5);
    expect((300 - v.x) / v.k).toBeCloseTo((300 - 10) / 1);
    expect((200 - v.y) / v.k).toBeCloseTo((200 - 20) / 1);
    expect(zoomAt({ x: 0, y: 0, k: 1 }, 100, 0, 0).k).toBe(MAX_ZOOM);
    expect(zoomAt({ x: 0, y: 0, k: 1 }, 0.001, 0, 0).k).toBe(MIN_ZOOM);
  });

  it('fits every card into the view, centred, without zooming in past 100%', () => {
    expect(fitView([], 800, 500)).toEqual({ x: 20, y: 20, k: 1 });
    const one = fitView([{ x: 0, y: 0 }], 800, 500);
    expect(one.k).toBe(1);
    expect(one.x + CARD_W / 2).toBeCloseTo(400);
    const wide = fitView([{ x: 0, y: 0 }, { x: 1200, y: 0 }], 800, 500);
    expect(wide.k).toBeLessThan(0.6);
    expect(wide.x + (1200 + CARD_W) * wide.k).toBeLessThanOrEqual(800);
  });
});
