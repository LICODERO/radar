export const BASE_W = 1440;
export const BASE_H = 900;
export const MIN_SCALE = 0.85;
export const FONT_MIN = 0.85;
export const FONT_MAX = 1.2;

export interface Fit {
  /** uniform scale of the 1440x900 stage */
  scale: number;
  /** multiplier for font sizes inside the stage so effective size stays in [FONT_MIN, FONT_MAX] */
  fs: number;
  left: number;
  top: number;
}

/** Proportional letterboxed fit. Fonts are clamped to 0.85x-1.2x of their base size. */
export function computeFit(vw: number, vh: number): Fit {
  if (!(vw > 0) || !(vh > 0)) return { scale: 1, fs: 1, left: 0, top: 0 };
  const scale = Math.max(MIN_SCALE, Math.min(vw / BASE_W, vh / BASE_H));
  const fs = Math.max(FONT_MIN, Math.min(FONT_MAX, scale)) / scale;
  return { scale, fs, left: (vw - BASE_W * scale) / 2, top: (vh - BASE_H * scale) / 2 };
}
