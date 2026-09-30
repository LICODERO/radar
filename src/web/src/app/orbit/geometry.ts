export const CENTER = 360;
export const R1 = 125;
export const R2 = 192;
export const R3 = 252;
export const R4 = 315;
export const REPO_START_DEG = -90;
export const WF_START_DEG = -70;

export type Pt = [number, number];

export const polar = (r: number, deg: number): Pt => {
  const t = (deg * Math.PI) / 180;
  return [CENTER + r * Math.cos(t), CENTER + r * Math.sin(t)];
};

export interface LineGeom {
  left: number;
  top: number;
  len: number;
  ang: number;
}

/** Line from p to q as a rotated 1px-high box (origin at p). */
export function lineBetween(p: Pt, q: Pt): LineGeom {
  const dx = q[0] - p[0];
  const dy = q[1] - p[1];
  return { left: p[0], top: p[1], len: Math.hypot(dx, dy), ang: (Math.atan2(dy, dx) * 180) / Math.PI };
}

export interface RingLayout {
  /** node diameter in px */
  size: number;
  /** number of alternating radial tracks (1 = classic single ring) */
  tracks: number;
  /** initials fit inside the node */
  labels: boolean;
  step: number;
  radiusOf(i: number): number;
  angleOf(i: number): number;
}

/**
 * Lays `count` nodes around a ring. Up to `maxSize` nodes keep full size (as in the mockup);
 * when they no longer fit, they shrink and alternate over up to `maxTracks` radial tracks.
 */
export function ringLayout(
  count: number,
  radius: number,
  startDeg: number,
  maxSize: number,
  minSize = 5,
  maxTracks = 4
): RingLayout {
  const n = Math.max(1, count);
  let tracks = 1;
  let size = maxSize;
  for (;;) {
    const arc = (2 * Math.PI * radius * tracks) / n;
    size = Math.max(minSize, Math.min(maxSize, arc * 0.9));
    if (size >= Math.min(maxSize, 12) || tracks >= maxTracks) break;
    tracks++;
  }
  const gap = tracks > 1 ? size * 0.95 : 0;
  const step = 360 / n;
  return {
    size,
    tracks,
    labels: size >= 20,
    step,
    radiusOf: (i) => radius + (tracks > 1 ? ((i % tracks) - (tracks - 1) / 2) * gap : 0),
    angleOf: (i) => startDeg + i * step
  };
}

export interface Spread {
  /** 'arc' = spread along the ring with `spacing` degrees, 'radial' = stack radially at the repo angle */
  mode: 'arc' | 'radial';
  spacing: number;
}

/**
 * Chooses how the k elements of one repo are placed on a ring so they stay clear of the neighbours.
 * `maxSpacing` is the mockup spacing, `minSpacing` the smallest readable one.
 */
export function chooseSpread(k: number, step: number, maxSpacing: number, minSpacing: number): Spread {
  if (k <= 1) return { mode: 'arc', spacing: 0 };
  const room = (step * 0.8) / (k - 1);
  const spacing = Math.min(maxSpacing, room);
  return spacing >= minSpacing ? { mode: 'arc', spacing } : { mode: 'radial', spacing: 0 };
}
