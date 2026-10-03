import { GapType } from './models';

export type Tool = 'claude' | 'codex';
/** shell flavour of the commands users paste: POSIX (macOS/Linux) or PowerShell (Windows) */
export type Shell = 'posix' | 'powershell';

export const GAP_ORDER: GapType[] = ['no-claude-md', 'no-agents', 'no-skills', 'workflow-not-linked', 'weak-files'];

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
export function buildCommand(shell: Shell, tool: Tool, dir: string, prompt: string, toolPath?: string | null): string {
  // with a path (what RADAR runs: the server found the tool, the terminal's own PATH may not have it) the tool is called by it
  const bin = toolPath ? (shell === 'powershell' ? `& ${quote(shell, toolPath)}` : quote(shell, toolPath)) : tool;
  return shell === 'powershell'
    ? `Set-Location -LiteralPath ${quote(shell, dir)}; ${bin} ${quote(shell, prompt)}`
    : `cd ${quote(shell, dir)} && ${bin} ${quote(shell, prompt)}`;
}

export function filterItems(items: readonly GapItem[], types: ReadonlySet<GapType>): GapItem[] {
  return items.filter((i) => types.has(i.type));
}

/** All commands as one script (each preceded by a comment), for the "copy all" button. `comment` renders the heading of one item. */
export function toScript(shell: Shell, tool: Tool, items: readonly GapItem[], comment: (i: GapItem) => string): string {
  return items.map((i) => `${comment(i)}\n${buildCommand(shell, tool, i.dir, i.prompt)}`).join('\n\n');
}

export function countByType(items: readonly GapItem[]): Record<GapType, number> {
  const c: Record<GapType, number> = { 'no-claude-md': 0, 'no-agents': 0, 'no-skills': 0, 'workflow-not-linked': 0, 'weak-files': 0 };
  for (const i of items) c[i.type]++;
  return c;
}
