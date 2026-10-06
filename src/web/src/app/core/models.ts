export type GapType = 'no-claude-md' | 'no-agents' | 'no-skills' | 'workflow-not-linked' | 'weak-files';

/** git's view of an agent, skill or workflow: tracked = public, ignored = private, not tracked and not ignored = untracked (shows up in git changes); unknown = git could not tell */
export type Visibility = 'public' | 'private' | 'untracked' | 'unknown';

export type Severity = 'error' | 'warning' | 'info';

/** One thing wrong with one AI file; `code` is turned into text by the UI (`quality.<code>`), `detail` is the offending value */
export interface QualityFinding {
  path: string;
  code: string;
  severity: Severity;
  detail?: string | null;
}

export interface FileQuality {
  path: string;
  kind: 'claude-md' | 'agent' | 'skill';
  score: number;
}

/** how good the existing files are (coverage only says they exist); `score` is null when the repo has no AI file; absent in scans before schema 2 */
export interface QualityInfo {
  score: number | null;
  files: FileQuality[];
  findings: QualityFinding[];
}

export interface AgentInfo {
  name: string;
  description: string;
  tools: string[];
  model?: string | null;
  path: string;
  /** absent in scans made before visibility was added */
  visibility?: Visibility | null;
}

export interface SkillInfo {
  name: string;
  description: string;
  path: string;
  visibility?: Visibility | null;
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
  quality?: QualityInfo | null;
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
  repos: { repoId: string; path: string; linked: boolean; agents?: string[]; skills?: string[]; visibility?: Visibility | null }[];
  issues: string[];
}

export interface VisibilityChange { kind: 'exclude-add' | 'exclude-remove' | 'git-rm-cached'; text: string }
/** what changing an item's visibility does (a plan unless `applied`); `blocked` says why nothing can be done */
export interface VisibilityOutcome {
  path: string;
  current: Visibility;
  target: 'private' | 'public';
  changes: VisibilityChange[];
  blocked: 'no-git' | 'exclude-damaged' | 'other-rule' | null;
  notes: ('commit-removal' | 'commit-to-share')[];
  applied: boolean;
  visibility: Visibility;
}

export interface CopyTarget { repoId: string; status: 'ready' | 'created' | 'exists' | 'unknown-repo' | 'repo-missing' | 'forbidden' | 'cannot-hide'; path?: string | null; hidden?: boolean }
export interface SkippedFile { path: string; reason: 'symlink' | 'binary' | 'unreadable' }
/** what a copy did or would do; for a skill `files`/`bytes`/`skipped` describe the folder that travels */
export interface CopyAgentResult { name: string; source: string; written: boolean; targets: CopyTarget[]; files?: string[]; bytes?: number; skipped?: SkippedFile[] }
export type CopyKind = 'agent' | 'skill';

/** one content of a shared agent/skill and the repos that hold it; `path` is the file in the first of them */
export interface ItemVariant { hash: string; repos: string[]; path: string }
/** an agent or skill with the same name in two or more repos; several variants mean the copies drifted apart */
export interface SharedItem {
  kind: 'agent' | 'skill';
  name: string;
  variants: ItemVariant[];
  /** repos of the same stack as a holder that lack it */
  missing: string[];
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
    avgQuality?: number | null;
  };
  repos: RepoInfo[];
  workflows: WorkflowInfo[];
  gaps: { repoId: string; type: GapType }[];
  warnings: { repoId: string; path: string; message: string }[];
  /** absent in scans before schema 2 */
  shared?: SharedItem[];
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
