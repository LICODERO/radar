import { describe, expect, it } from 'vitest';
import { RepoInfo, WorkflowInfo } from '../core/models';
import { buildScene } from './orbit-scene';

const repo = (id: string, o: Partial<RepoInfo> = {}): RepoInfo => ({
  id, name: id, path: id, initials: id.slice(0, 2).toUpperCase(), stack: '.NET', stacks: ['.NET'],
  claudeMd: { exists: true, path: 'CLAUDE.md' }, agents: [], skills: [],
  outputs: { exists: false, path: '', notes: 0 },
  coverage: { score: 50, parts: { claudeMd: 40, agents: 0, skills: 0, outputs: 0 } }, gaps: [], ...o
});
const agent = (name: string) => ({ name, description: name + ' desc', tools: ['Read'], model: null, path: `.claude/agents/${name}.md` });
const skill = (name: string) => ({ name, description: name + ' desc', path: `.claude/skills/${name}/SKILL.md` });

const repos = [
  repo('alpha', { agents: [agent('a1'), agent('a2')], skills: [skill('s1')] }),
  repo('beta', { agents: [agent('a1')], claudeMd: { exists: false, path: 'CLAUDE.md' }, coverage: { score: 10, parts: { claudeMd: 0, agents: 25, skills: 0, outputs: 0 } } }),
  repo('gamma')
];
const wfs: WorkflowInfo[] = [
  { id: 'W1', name: 'commit', description: 'd', when: 'w', agents: [], repos: [{ repoId: 'alpha', path: 'p', linked: true }], issues: [] },
  { id: 'W2', name: 'release', description: 'd', when: 'w', agents: ['a1'], repos: [{ repoId: 'beta', path: 'p', linked: false }], issues: [] }
];
const base = { repos, workflows: wfs, selId: 'alpha', pick: null, hover: null, matches: null, tick: 0 };

describe('buildScene', () => {
  it('creates one node per repo, agent, skill and workflow', () => {
    const s = buildScene(base);
    expect(s.repos).toHaveLength(3);
    expect(s.agents).toHaveLength(3);
    expect(s.skills).toHaveLength(1);
    expect(s.wfs).toHaveLength(2);
  });

  it('marks repos without CLAUDE.md as dashed and colours by coverage', () => {
    const s = buildScene(base);
    expect(s.repos.find((r) => r.id === 'beta')!.dashed).toBe(true);
    expect(s.repos.find((r) => r.id === 'beta')!.color).toBe('#ff4fa3');
    expect(s.repos.find((r) => r.id === 'alpha')!.color).toBe('#ffb84d');
  });

  it('draws lines from the selected repo to its agents, skills and workflows', () => {
    const s = buildScene(base);
    expect(s.lines).toHaveLength(2 + 1 + 1);
  });

  it('highlights nothing while no repo is selected', () => {
    const s = buildScene({ ...base, selId: null });
    expect(s.lines).toHaveLength(0);
    expect(s.halo).toBeNull();
    expect(s.rip).toBeNull();
  });

  it('adds empty slots for missing agents and skills', () => {
    expect(buildScene(base).slots).toHaveLength(1 + 2 + 1 - 1); // beta: skills; gamma: agents + skills
  });

  it('lights up workflows of the selected repo', () => {
    const s = buildScene(base);
    expect(s.wfs.find((w) => w.id === 'W1')!.color).toBe('#ffffff');
    expect(s.wfs.find((w) => w.id === 'W2')!.color).toBe('#8a90a8');
  });

  it('builds a popover with usage for a picked agent and draws dashed lines to other repos', () => {
    const s = buildScene({ ...base, pick: { kind: 'a', repoId: 'alpha', name: 'a1' } });
    expect(s.pop?.title).toBe('a1');
    expect(s.pop?.rows.find((r) => r.k === 'UŻYWANY W')!.v).toContain('2 repo');
    expect(s.lines.some((l) => l.dashed && l.op === 0.75)).toBe(true);
    expect(s.pop?.rows.find((r) => r.k === 'ŚCIEŻKA')!.v).toBe('alpha/.claude/agents/a1.md');
  });

  it('describes a workflow without agents as a plain procedure', () => {
    const s = buildScene({ ...base, pick: { kind: 'w', name: 'W1' } });
    expect(s.pop?.rows.find((r) => r.k === 'KROKI')!.v).toContain('bez agentów');
  });

  it('dims repos that do not match the search', () => {
    const s = buildScene({ ...base, matches: new Set(['alpha']) });
    expect(s.repos.find((r) => r.id === 'alpha')!.op).toBe(1);
    expect(s.repos.find((r) => r.id === 'gamma')!.op).toBeLessThan(0.3);
  });

  it('alternates animation names so they restart on every interaction', () => {
    expect(buildScene({ ...base, tick: 0 }).lines[0].anim).toBe('growA');
    expect(buildScene({ ...base, tick: 1 }).lines[0].anim).toBe('growB');
  });

  it('shows a tooltip for a hovered repo', () => {
    const s = buildScene({ ...base, hover: { kind: 'r', name: 'beta' } });
    expect(s.tip?.l1).toBe('beta');
    expect(s.tip?.l2).toContain('10%');
  });

  it('copes with an empty scan', () => {
    const s = buildScene({ ...base, repos: [], workflows: [], selId: null });
    expect(s.repos).toHaveLength(0);
    expect(s.halo).toBeNull();
  });

  it('stays responsive for hundreds of repos (no spokes/slots clutter)', () => {
    const many = Array.from({ length: 400 }, (_, i) => repo('r' + i, { agents: [agent('a1'), agent('a2')] }));
    const s = buildScene({ ...base, repos: many, workflows: [], selId: 'r0' });
    expect(s.repos).toHaveLength(400);
    expect(s.spokes).toHaveLength(0);
    expect(s.slots).toHaveLength(0);
  });
});
