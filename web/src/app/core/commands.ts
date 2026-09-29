import { GapType } from './models';

export type Tool = 'claude' | 'codex';
/** shell flavour of the commands users paste: POSIX (macOS/Linux) or PowerShell (Windows) */
export type Shell = 'posix' | 'powershell';

export const GAP_ORDER: GapType[] = ['no-claude-md', 'no-agents', 'no-skills', 'no-outputs', 'workflow-not-linked'];

export const GAP_LABELS: Record<GapType, string> = {
  'no-claude-md': 'BRAK CLAUDE.md',
  'no-agents': 'BRAK AGENTÓW',
  'no-skills': 'BRAK SKILLI',
  'no-outputs': 'BRAK OUTPUTS',
  'workflow-not-linked': 'WORKFLOW BEZ ODSYŁACZA'
};

/** One command to fill one gap. Prompts and directories come from the server (single source of truth). */
export interface GapItem {
  repoId: string;
  repoName: string;
  initials: string;
  type: GapType;
  /** absolute repo directory, native to the server's OS */
  dir: string;
  prompt: string;
}

/** Single-quotes a string for the given shell. */
export function quote(shell: Shell, s: string): string {
  return shell === 'powershell' ? `'${s.replace(/'/g, `''`)}'` : `'${s.replace(/'/g, `'\\''`)}'`;
}

/** `cd <repo> && claude '<prompt>'` (POSIX) or `Set-Location ...; claude '<prompt>'` (PowerShell). */
export function buildCommand(shell: Shell, tool: Tool, dir: string, prompt: string): string {
  return shell === 'powershell'
    ? `Set-Location -LiteralPath ${quote(shell, dir)}; ${tool} ${quote(shell, prompt)}`
    : `cd ${quote(shell, dir)} && ${tool} ${quote(shell, prompt)}`;
}

export function filterItems(items: readonly GapItem[], types: ReadonlySet<GapType>): GapItem[] {
  return items.filter((i) => types.has(i.type));
}

/** All commands as one script (each preceded by a comment), for the "copy all" button. */
export function toScript(shell: Shell, tool: Tool, items: readonly GapItem[]): string {
  return items.map((i) => `# ${i.repoName} · ${GAP_LABELS[i.type]}\n${buildCommand(shell, tool, i.dir, i.prompt)}`).join('\n\n');
}

export function countByType(items: readonly GapItem[]): Record<GapType, number> {
  const c: Record<GapType, number> = { 'no-claude-md': 0, 'no-agents': 0, 'no-skills': 0, 'no-outputs': 0, 'workflow-not-linked': 0 };
  for (const i of items) c[i.type]++;
  return c;
}
