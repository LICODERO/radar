export type GapType = 'no-claude-md' | 'no-agents' | 'no-skills' | 'workflow-not-linked';

export interface AgentInfo {
  name: string;
  description: string;
  tools: string[];
  model?: string | null;
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
  coverage: { score: number; parts: { claudeMd: number; agents: number; skills: number } };
  gaps: GapType[];
}

export interface WorkflowInfo {
  id: string;
  name: string;
  description: string;
  when: string;
  agents: string[];
  /** skills the workflow uses (optional `skills:` frontmatter; absent in older scans) */
  skills?: string[];
  /** `agents`/`skills`: what this repo's copy of the workflow names; they are elements of that same repo (absent in older scans) */
  repos: { repoId: string; path: string; linked: boolean; agents?: string[]; skills?: string[] }[];
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

export interface Settings {
  scanPath: string | null;
  scanPathDisplay: string;
  exists: boolean;
  maxDepth: number;
  canPickFolder: boolean;
}

export interface FileContent {
  path: string;
  content: string;
  truncated: boolean;
  bytes: number;
}

export interface ToolsInfo {
  platform: 'macos' | 'windows' | 'linux' | 'other' | string;
  shell: 'posix' | 'powershell';
  canLaunch: boolean;
  /** the terminal app that opens (iTerm2, Terminal.app...); absent when it is just "the terminal" */
  terminal?: string;
  tools: Record<string, boolean>;
  /** where the server found each tool (absolute path), null when it did not; absent from older servers */
  toolPaths?: Record<string, string | null>;
}

/** `GET /api/tools/{tool}`: is the CLI installed, where, and which version it reports */
export interface ToolStatus {
  tool: string;
  found: boolean;
  path: string | null;
  version: string | null;
}

export interface AgentDraft {
  name: string;
  path: string | null;
  content: string;
  valid: boolean;
  errors: string[];
  costUsd?: number;
}

/** Second brain: a vault folder outside the repos (see `Features/Vault` on the server). */
export interface VaultStatus {
  /** 'missing' = a path is saved but the folder is gone or no longer a vault */
  state: 'none' | 'ok' | 'missing';
  path?: string | null;
  pathDisplay: string;
  reason?: 'folder-missing' | 'not-a-vault' | 'unsupported-version' | null;
  projects: { name: string; repoId?: string | null }[];
  suggestedPath: string;
  suggestedPathDisplay: string;
}

export type SkillState = 'not-installed' | 'up-to-date' | 'outdated' | 'modified' | 'foreign' | 'linked';

export interface SkillStatus {
  name: string;
  state: SkillState;
  path: string;
  pathDisplay: string;
  availableVersion: number;
  installedVersion?: number | null;
}

/** What enabling the second brain for a repo would write (or just wrote). */
export interface ProjectPlan {
  vaultDir: string;
  creates: string[];
  claudeLocalPath: string;
  claudeLocalExists: boolean;
  block: string;
  alreadyEnabled: boolean;
  /** whether git ignores CLAUDE.local.md in the repo; absent when git could not tell */
  gitIgnored?: boolean | null;
}

export interface EnableProjectResult {
  applied: boolean;
  plan: ProjectPlan;
  vault: VaultStatus;
}
