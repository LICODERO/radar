import { MsgKey } from '../i18n/pl';

export const TOUR_KEY = 'radar.tour';

export interface Rect { x: number; y: number; w: number; h: number }
export type Side = 'right' | 'left' | 'below' | 'center';

export interface TourStep {
  id: string;
  /** value of the `data-tour` attribute of the highlighted element; null for a step with nothing highlighted */
  target: string | null;
  /** fixed stage rectangle, for a target that has no element of its own (the orbit) */
  rect?: Rect;
  round?: boolean;
  side: Side;
  /** clicks reach the highlighted element (choose the folder, press SCAN); on the other steps the page is read-only */
  interactive: boolean;
  title: MsgKey;
  text: MsgKey;
}

/** Stage 1 (welcome, folder, scan) works with no scan yet; stage 2 (orbit onwards) starts once the first scan has finished. */
export const TOUR_STEPS: TourStep[] = [
  { id: 'welcome', target: null, side: 'center', interactive: false, title: 'tour.welcome.title', text: 'tour.welcome.text' },
  { id: 'path', target: 'scan-path', side: 'below', interactive: true, title: 'tour.path.title', text: 'tour.path.text' },
  { id: 'scan', target: 'scan-btn', side: 'below', interactive: true, title: 'tour.scan.title', text: 'tour.scan.text' },
  { id: 'orbit', target: null, rect: { x: 360, y: 100, w: 720, h: 720 }, round: true, side: 'right', interactive: false, title: 'tour.orbit.title', text: 'tour.orbit.text' },
  { id: 'repos', target: 'repos', side: 'right', interactive: false, title: 'tour.repos.title', text: 'tour.repos.text' },
  { id: 'tiles', target: 'tiles', side: 'left', interactive: false, title: 'tour.tiles.title', text: 'tour.tiles.text' },
  { id: 'tools', target: 'tools', side: 'right', interactive: false, title: 'tour.tools.title', text: 'tour.tools.text' },
  { id: 'actions', target: 'actions', side: 'left', interactive: false, title: 'tour.actions.title', text: 'tour.actions.text' }
];

/** the step with the SCAN button; the first scan that ends while the tour is on stage 1 continues with the step after it */
export const SCAN_STEP = TOUR_STEPS.findIndex((s) => s.id === 'scan');

/**
 * Where the card goes: beside the highlighted rectangle (flipped to the other side when it does not fit), kept inside the stage.
 * Nothing highlighted, or side 'center': in the middle.
 */
export function placeCard(target: Rect | null, side: Side, card: { w: number; h: number }, stage: { w: number; h: number }, gap = 16, margin = 16): { x: number; y: number } {
  const clamp = (v: number, size: number, max: number) => Math.max(margin, Math.min(v, max - size - margin));
  if (!target || side === 'center') return { x: (stage.w - card.w) / 2, y: (stage.h - card.h) / 2 };
  let x: number;
  let y = target.y;
  if (side === 'below') {
    x = target.x;
    y = target.y + target.h + gap;
  } else {
    const right = target.x + target.w + gap;
    const left = target.x - card.w - gap;
    const fitsRight = right + card.w <= stage.w - margin;
    const fitsLeft = left >= margin;
    x = side === 'right' ? (fitsRight || !fitsLeft ? right : left) : (fitsLeft || !fitsRight ? left : right);
  }
  return { x: clamp(x, card.w, stage.w), y: clamp(y, card.h, stage.h) };
}

/** True once the tour was finished or skipped. Storage can be unavailable (private window), then the tour simply shows again next time. */
export function tourSeen(): boolean {
  try { return localStorage.getItem(TOUR_KEY) === 'done'; } catch { return false; }
}

export function markTourSeen(): void {
  try { localStorage.setItem(TOUR_KEY, 'done'); } catch { /* the tour shows again next time */ }
}
