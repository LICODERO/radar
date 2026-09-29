import { GapType, RepoInfo, ScanResult } from './models';

export type Tool = 'claude' | 'codex';

export const GAP_ORDER: GapType[] = ['no-claude-md', 'no-agents', 'no-skills', 'no-outputs', 'workflow-not-linked'];

export const GAP_LABELS: Record<GapType, string> = {
  'no-claude-md': 'BRAK CLAUDE.md',
  'no-agents': 'BRAK AGENTÓW',
  'no-skills': 'BRAK SKILLI',
  'no-outputs': 'BRAK OUTPUTS',
  'workflow-not-linked': 'WORKFLOW BEZ ODSYŁACZA'
};

export interface GapItem {
  repoId: string;
  repoName: string;
  initials: string;
  type: GapType;
  /** absolute repo directory */
  dir: string;
  prompt: string;
}

/** Single-quotes a string for a POSIX shell. */
export function shq(s: string): string {
  return `'${s.replace(/'/g, `'\\''`)}'`;
}

export function promptFor(type: GapType, unlinkedWorkflows: string[] = []): string {
  switch (type) {
    case 'no-claude-md':
      return 'Analyze this repository and create a CLAUDE.md in its root. Include: what the project is, the tech stack, the exact build, test and run commands you can verify from the repository files, the architecture and folder structure, and the coding conventions you can infer. Do not invent commands or facts that are not visible in the repository. Keep it concise (under 120 lines) and do not modify any other file.';
    case 'no-agents':
      return 'Create the directory .claude/agents and add 1-3 subagents that would be useful in this repository (for example a code reviewer and a test writer), based on its stack and conventions. Each agent is a Markdown file with the frontmatter fields name, description and tools, followed by short instructions. Do not modify any other file.';
    case 'no-skills':
      return 'Create the directory .claude/skills and add 1-3 skills for recurring procedures in this repository (for example build and test, database migrations, conventions). Each skill is a folder with a SKILL.md that has the frontmatter fields name and description followed by concrete steps. Base them only on what exists in the repository and do not modify any other file.';
    case 'no-outputs':
      return 'Create .claude/memory/OUTPUTS.md as an index of project notes, plus .claude/memory/project.md describing the goal, tech stack and constraints of this repository as far as they are visible in it. Keep every entry in the index to one line. Do not modify any other file.';
    case 'workflow-not-linked':
      return `CLAUDE.md does not mention these workflow files: ${unlinkedWorkflows.join(', ')}. Read each one and add a short table to CLAUDE.md (create the file if it does not exist) with the columns workflow, when to use, file path, taking the name and when fields from the frontmatter. Reference the files by path only and do not import them with @. Do not modify any other file.`;
  }
}

/** `cd <repo> && claude '<prompt>'` (or codex). The commands are only shown and copied, never run by the app. */
export function buildCommand(tool: Tool, dir: string, prompt: string): string {
  return `cd ${shq(dir)} && ${tool === 'claude' ? 'claude' : 'codex'} ${shq(prompt)}`;
}

export function gapCounts(result: ScanResult | null): Record<GapType, number> {
  const c: Record<GapType, number> = { 'no-claude-md': 0, 'no-agents': 0, 'no-skills': 0, 'no-outputs': 0, 'workflow-not-linked': 0 };
  for (const r of result?.repos ?? []) for (const g of r.gaps) c[g]++;
  return c;
}

const repoDir = (root: string, r: RepoInfo) => `${root.replace(/\/+$/, '')}/${r.path}`;

export function gapItems(result: ScanResult | null, types: ReadonlySet<GapType>): GapItem[] {
  if (!result) return [];
  const items: GapItem[] = [];
  for (const r of result.repos) {
    for (const type of GAP_ORDER) {
      if (!types.has(type) || !r.gaps.includes(type)) continue;
      const unlinked = type === 'workflow-not-linked'
        ? result.workflows.flatMap((w) => w.repos.filter((x) => x.repoId === r.id && !x.linked).map((x) => x.path))
        : [];
      items.push({ repoId: r.id, repoName: r.name, initials: r.initials, type, dir: repoDir(result.scanRoot, r), prompt: promptFor(type, unlinked) });
    }
  }
  return items;
}

/** All commands as one shell script (each preceded by a comment), for the "copy all" button. */
export function toScript(tool: Tool, items: GapItem[]): string {
  return items.map((i) => `# ${i.repoName} · ${GAP_LABELS[i.type]}\n${buildCommand(tool, i.dir, i.prompt)}`).join('\n\n');
}
