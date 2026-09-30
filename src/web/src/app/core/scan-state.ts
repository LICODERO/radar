export interface ScanCounters {
  repos: number;
  agents: number;
  skills: number;
  gaps: number;
}

export interface ScanLogItem {
  id: string;
  name: string;
  hasClaudeMd: boolean;
  agents: number;
  skills: number;
}

export interface ScanBlip {
  index: number;
  coverage: number;
}

export interface ScanUiState {
  status: 'running' | 'done' | 'error' | 'cancelled';
  phase: string;
  /** null while the total is unknown (directory walk) */
  percent: number | null;
  found: number;
  total: number | null;
  current: string;
  log: ScanLogItem[];
  blips: ScanBlip[];
  counters: ScanCounters;
  workflows: number;
  durationMs: number | null;
  message: string | null;
}

export const LOG_LINES = 9;

export const initialScan = (): ScanUiState => ({
  status: 'running', phase: 'init', percent: 0, found: 0, total: null, current: '',
  log: [], blips: [], counters: { repos: 0, agents: 0, skills: 0, gaps: 0 }, workflows: 0, durationMs: null, message: null
});

/** Folds one server-sent event into the overlay state (pure, so it can be unit tested). */
export function applyScanEvent(s: ScanUiState, name: string, data: any): ScanUiState {
  switch (name) {
    case 'started':
      return { ...initialScan() };
    case 'phase':
      return {
        ...s, phase: data.phase, percent: data.percent ?? null,
        workflows: data.workflows ?? s.workflows
      };
    case 'repo-found':
      return { ...s, found: data.found, current: data.name };
    case 'repo-scanned':
      return {
        ...s,
        total: data.total,
        percent: data.percent,
        current: data.name,
        counters: data.counters,
        log: [...s.log, { id: data.id, name: data.name, hasClaudeMd: data.hasClaudeMd, agents: data.agents, skills: data.skills }].slice(-LOG_LINES),
        blips: [...s.blips, { index: data.index - 1, coverage: data.coverage }]
      };
    case 'completed':
      return { ...s, status: 'done', phase: 'done', percent: 100, durationMs: data.durationMs, workflows: data.summary?.workflows ?? s.workflows };
    case 'cancelled':
      return { ...s, status: 'cancelled' };
    case 'error':
      return { ...s, status: 'error', message: data.message ?? null };
    default:
      return s;
  }
}

/** Blip position on the 440px scan radar: angle by index, distance by coverage (as in the mockup). */
export function blipPosition(index: number, total: number, coverage: number): { left: number; top: number } {
  const t = ((-90 + (index * 360) / Math.max(1, total)) * Math.PI) / 180;
  const rad = 190 - coverage * 1.5;
  return { left: 220 + rad * Math.cos(t) - 6, top: 220 + rad * Math.sin(t) - 6 };
}
