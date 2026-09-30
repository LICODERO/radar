export const COLOR = {
  bg: '#04050a',
  lime: '#c6ff3d',
  violet: '#a99bff',
  white: '#e6e9f2',
  workflow: '#4dd6ff',
  magenta: '#ff4fa3',
  amber: '#ffb84d'
} as const;

/** Repo colour by coverage: white >= 70, amber 40-69, magenta < 40 (as in the mockup). */
export function coverageColor(score: number): string {
  return score >= 70 ? '#ffffff' : score >= 40 ? COLOR.amber : COLOR.magenta;
}
