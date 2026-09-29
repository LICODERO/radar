export type GapType = 'no-claude-md' | 'no-agents' | 'no-skills' | 'no-outputs' | 'workflow-not-linked';

export interface AgentInfo {
  name: string;
  description: string;
  tools: string[];
  model: string | null;
  path: string;
}

export interface SkillInfo {
  name: string;
  description: string;
  path: string;
}

export interface RepoInfo {
  id: string;
  name: string;
  path: string;
  initials: string;
  stack: string;
  stacks: string[];
  claudeMd: { exists: boolean; path: string };
  agents: AgentInfo[];
  skills: SkillInfo[];
  outputs: { exists: boolean; path: string; notes: number };
  coverage: { score: number; parts: { claudeMd: number; agents: number; skills: number; outputs: number } };
  gaps: GapType[];
}

export interface WorkflowInfo {
  id: string;
  name: string;
  description: string;
  when: string;
  agents: string[];
  repos: { repoId: string; path: string; linked: boolean }[];
  issues: string[];
}

export interface ScanResult {
  schemaVersion: number;
  sample?: boolean;
  scanRoot: string;
  scannedAt: string;
  durationMs: number;
  summary: {
    repos: number;
    agents: number;
    skills: number;
    workflows: number;
    gaps: number;
    stacks: string[];
    avgCoverage: number;
  };
  repos: RepoInfo[];
  workflows: WorkflowInfo[];
  gaps: { repoId: string; type: GapType }[];
  warnings: { repoId: string; path: string; message: string }[];
}

export type PickKind = 'a' | 's' | 'w';
export interface Pick {
  kind: PickKind;
  /** repo id for agents/skills, undefined for workflows */
  repoId?: string;
  /** agent/skill name or workflow id */
  name: string;
}
export interface Hover {
  kind: 'r' | PickKind;
  repoId?: string;
  name: string;
}
