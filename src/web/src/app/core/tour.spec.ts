import { describe, expect, it } from 'vitest';
import { SCAN_STEP, TOUR_STEPS, placeCard } from './tour';

const stage = { w: 1440, h: 900 };
const card = { w: 340, h: 200 };

describe('tour steps', () => {
  it('has the SCAN step right after the folder step, and orbit onwards behind it', () => {
    expect(TOUR_STEPS.map((s) => s.id)).toEqual(['welcome', 'path', 'scan', 'orbit', 'repos', 'tiles', 'tools', 'actions']);
    expect(TOUR_STEPS[SCAN_STEP].id).toBe('scan');
    expect(TOUR_STEPS.filter((s) => s.interactive).map((s) => s.id)).toEqual(['path', 'scan']);
  });
});

describe('placeCard', () => {
  it('puts the card in the middle when nothing is highlighted', () => {
    expect(placeCard(null, 'center', card, stage)).toEqual({ x: 550, y: 350 });
  });

  it('puts the card to the right of the target, level with its top', () => {
    expect(placeCard({ x: 36, y: 104, w: 300, h: 400 }, 'right', card, stage)).toEqual({ x: 352, y: 104 });
  });

  it('flips to the left when there is no room on the right', () => {
    expect(placeCard({ x: 1104, y: 104, w: 300, h: 150 }, 'right', card, stage)).toEqual({ x: 748, y: 104 });
  });

  it('flips to the right when there is no room on the left', () => {
    expect(placeCard({ x: 36, y: 104, w: 300, h: 150 }, 'left', card, stage)).toEqual({ x: 352, y: 104 });
  });

  it('puts the card under the target and keeps it inside the stage', () => {
    expect(placeCard({ x: 1276, y: 28, w: 128, h: 38 }, 'below', card, stage)).toEqual({ x: 1084, y: 82 });
  });

  it('keeps the card above the bottom edge', () => {
    expect(placeCard({ x: 36, y: 780, w: 300, h: 100 }, 'right', card, stage).y).toBe(684);
  });
});
