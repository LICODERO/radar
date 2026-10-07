import { FlowAuth, FlowKind, FlowRule } from '../core/models';

export const CARD_W = 210;
export const CARD_H = 70;
/** the board has no walls: cards may sit anywhere inside this (generous) box, around the origin */
export const WORLD_MIN = -4000;
export const WORLD_MAX = 6000;
export const MIN_ZOOM = 0.4;
export const MAX_ZOOM = 1.6;

export interface Pos { x: number; y: number }

/** variable names only: a value (or anything with spaces and signs) is not a name */
const ENV_NAME = /^[A-Za-z_][A-Za-z0-9_.-]{0,63}$/;

/** request/response kinds: the caller asks, the provider answers, so there is nothing to choose about the direction */
export const isSync = (k: FlowKind): boolean => k === 'rest' || k === 'graphql' || k === 'grpc';

export const ruleKey = (r: Pick<FlowRule, 'from' | 'to' | 'kind'>): string => `${r.from}\u0000${r.to}\u0000${r.kind}`;

export function newRule(from: string, to: string, kind: FlowKind = 'rest', auth: FlowAuth = 'none'): FlowRule {
  return { from, to, kind, auth, authEnv: [], contract: null, notes: null, visibility: 'public', direction: 'out' };
}

/** the kind a new rule between the two repos can still take (one rule per from/to/kind), or null when all are used */
export function freeKind(rules: FlowRule[], from: string, to: string): FlowKind | null {
  const order: FlowKind[] = ['rest', 'events', 'graphql', 'grpc', 'db', 'files', 'other'];
  return order.find((k) => !rules.some((r) => r.from === from && r.to === to && r.kind === k)) ?? null;
}

/** splits "A_ID, B_SECRET" into names; `bad` lists what is not a variable name (so the UI can refuse a pasted secret) */
export function parseEnv(text: string): { names: string[]; bad: string[] } {
  const parts = text.split(/[\s,;]+/).map((p) => p.trim()).filter(Boolean);
  const names: string[] = [];
  const bad: string[] = [];
  for (const p of parts) {
    if (!ENV_NAME.test(p)) bad.push(p);
    else if (!names.includes(p)) names.push(p);
  }
  return { names, bad };
}

/** the same set of rules, whatever the order */
export function sameRules(a: FlowRule[], b: FlowRule[]): boolean {
  if (a.length !== b.length) return false;
  const norm = (r: FlowRule) => JSON.stringify([r.from, r.to, r.kind, r.auth, [...r.authEnv].sort(), r.contract ?? null, r.notes ?? null, r.visibility, r.direction]);
  const x = a.map(norm).sort();
  const y = b.map(norm).sort();
  return x.every((v, i) => v === y[i]);
}

export type Side = 'top' | 'right' | 'bottom' | 'left';
export const SIDES: readonly Side[] = ['top', 'right', 'bottom', 'left'];

/** the unit vector pointing out of a card on that side */
export function normal(s: Side): Pos {
  return s === 'top' ? { x: 0, y: -1 } : s === 'bottom' ? { x: 0, y: 1 } : s === 'left' ? { x: -1, y: 0 } : { x: 1, y: 0 };
}

/** the dot on that side of a card (its middle) */
export function portPos(card: Pos, s: Side): Pos {
  return s === 'top' ? { x: card.x + CARD_W / 2, y: card.y } : s === 'bottom' ? { x: card.x + CARD_W / 2, y: card.y + CARD_H }
    : s === 'left' ? { x: card.x, y: card.y + CARD_H / 2 } : { x: card.x + CARD_W, y: card.y + CARD_H / 2 };
}

/** the side of `from` that looks towards `to`: left or right when the cards are further apart sideways, top or bottom otherwise */
export function facing(from: Pos, to: Pos): Side {
  const dx = to.x - from.x;
  const dy = (to.y - from.y) * (CARD_W / CARD_H);          // the cards are wider than tall, so a vertical gap weighs more
  return Math.abs(dx) >= Math.abs(dy) ? (dx >= 0 ? 'right' : 'left') : (dy >= 0 ? 'bottom' : 'top');
}

/** a point `d` px away from `p` along the side's normal (where an arrow head stops, or a curve handle sits) */
export function away(p: Pos, s: Side, d: number): Pos {
  const n = normal(s);
  return { x: p.x + n.x * d, y: p.y + n.y * d };
}

export interface Curve { d: string; mid: Pos }

/** the curve between two dots, leaving and entering along their sides' normals */
export function curve(p1: Pos, s1: Side, p2: Pos, s2: Side | null): Curve {
  const reach = Math.max(50, Math.hypot(p2.x - p1.x, p2.y - p1.y) / 3);
  const c1 = away(p1, s1, reach);
  const c2 = s2 ? away(p2, s2, reach) : p2;
  return {
    d: `M ${p1.x} ${p1.y} C ${c1.x} ${c1.y}, ${c2.x} ${c2.y}, ${p2.x} ${p2.y}`,
    mid: { x: (p1.x + 3 * c1.x + 3 * c2.x + p2.x) / 8, y: (p1.y + 3 * c1.y + 3 * c2.y + p2.y) / 8 }
  };
}

/** keeps a card inside the (very large) world, so a runaway drag cannot lose it */
export function clampPos(p: Pos): Pos {
  const c = (v: number, size: number) => Math.min(WORLD_MAX - size, Math.max(WORLD_MIN, Math.round(v)));
  return { x: c(p.x, CARD_W), y: c(p.y, CARD_H) };
}

/** a free slot for a new card: the grid cell closest to the top left that no card covers */
export function freeSpot(taken: Pos[]): Pos {
  const gx = CARD_W + 80;
  const gy = CARD_H + 60;
  for (let i = 0; i < 48; i++) {
    const p = { x: 30 + (i % 4) * gx, y: 30 + Math.floor(i / 4) * gy };
    if (!taken.some((t) => Math.abs(t.x - p.x) < CARD_W && Math.abs(t.y - p.y) < CARD_H)) return p;
  }
  return { x: 30 + (taken.length % 5) * 24, y: 30 + (taken.length % 5) * 24 };
}

/** what the board shows: its top left corner in the world (screen px) and the zoom */
export interface View { x: number; y: number; k: number }

export const clampZoom = (k: number): number => Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, k));

/** zooms by `factor` and keeps the world point under (px, py) where it was */
export function zoomAt(v: View, factor: number, px: number, py: number): View {
  const k = clampZoom(v.k * factor);
  const r = k / v.k;
  return { k, x: px - (px - v.x) * r, y: py - (py - v.y) * r };
}

/** the view that shows every card, centred, never zoomed in past 100% */
export function fitView(cards: Pos[], w: number, h: number, pad = 40): View {
  if (cards.length === 0 || w <= 0 || h <= 0) return { x: 20, y: 20, k: 1 };
  const minX = Math.min(...cards.map((c) => c.x));
  const minY = Math.min(...cards.map((c) => c.y));
  const bw = Math.max(...cards.map((c) => c.x)) + CARD_W - minX;
  const bh = Math.max(...cards.map((c) => c.y)) + CARD_H - minY;
  const k = clampZoom(Math.min(1, (w - 2 * pad) / bw, (h - 2 * pad) / bh));
  return { k, x: (w - bw * k) / 2 - minX * k, y: (h - bh * k) / 2 - minY * k };
}

const STORAGE_KEY = 'radar.flow.layout';

/** card positions are only a convenience of this browser, not part of the rules */
export function loadLayout(scope: string): Record<string, Pos> {
  try {
    const all = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}') as Record<string, Record<string, Pos>>;
    const mine = all[scope] ?? {};
    return Object.fromEntries(Object.entries(mine).filter(([, p]) => Number.isFinite(p?.x) && Number.isFinite(p?.y)).map(([k, p]) => [k, clampPos(p)]));
  } catch { return {}; }
}

export function saveLayout(scope: string, layout: Record<string, Pos>): void {
  try {
    const all = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}') as Record<string, Record<string, Pos>>;
    all[scope] = layout;
    localStorage.setItem(STORAGE_KEY, JSON.stringify(all));
  } catch { /* storage unavailable */ }
}

const PORTS_KEY = 'radar.flow.ports';

/** which dots a connection uses is a drawing choice of this browser, like the card positions */
export interface PortPick { a: Side; b: Side }

export function loadPorts(): Record<string, PortPick> {
  try {
    const all = JSON.parse(localStorage.getItem(PORTS_KEY) ?? '{}') as Record<string, PortPick>;
    return Object.fromEntries(Object.entries(all).filter(([, v]) => SIDES.includes(v?.a) && SIDES.includes(v?.b)));
  } catch { return {}; }
}

export function savePorts(ports: Record<string, PortPick>): void {
  try { localStorage.setItem(PORTS_KEY, JSON.stringify(ports)); } catch { /* storage unavailable */ }
}
