import { describe, expect, it } from 'vitest';
import { RepoInfo, WorkflowInfo } from '../core/models';
import { buildScene } from './orbit-scene';

const repo = (id: string, o: Partial<RepoInfo> = {}): RepoInfo => ({
  id, name: id, path: id, initials: id.slice(0, 2).toUpperCase(), stack: '.NET', stacks: ['.NET'],
  claudeMd: { exists: true, path: 'CLAUDE.md' }, agents: [], skills: [],
  coverage: { score: 50, parts: { claudeMd: 40, agents: 0, skills: 0 } }, gaps: [], ...o
});
const agent = (name: string) => ({ name, description: name + ' desc', tools: ['Read'], model: null, path: `.claude/agents/${name}.md` });
const skill = (name: string) => ({ name, description: name + ' desc', path: `.claude/skills/${name}/SKILL.md` });

const repos = [
  repo('alpha', { agents: [agent('a1'), agent('a2')], skills: [skill('s1')] }),
  repo('beta', { agents: [agent('a1')], claudeMd: { exists: false, path: 'CLAUDE.md' }, coverage: { score: 10, parts: { claudeMd: 0, agents: 30, skills: 0 } } }),
  repo('gamma')
];
const wfs: WorkflowInfo[] = [
  { id: 'W1', name: 'commit', description: 'd', when: 'w', agents: [], repos: [{ repoId: 'alpha', path: 'p', linked: true }], issues: [] },
  { id: 'W2', name: 'release', description: 'd', when: 'w', agents: ['a1'], repos: [{ repoId: 'beta', path: 'p', linked: false }], issues: [] }
];
const base = { repos, workflows: wfs, selId: 'alpha', pick: null, hover: null, matches: null, tick: 0, lang: 'pl' as const };

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
    expect(buildScene({ ...base, selId: null }).slots).toHaveLength(1 + 2 + 1 - 1); // beta: skills; gamma: agents + skills
  });

  describe('with a repo selected', () => {
    it('fades out other repos, their elements, unrelated workflows, spokes and slots', () => {
      const s = buildScene({ ...base, selId: 'gamma' });
      expect(s.repos.filter((r) => !r.off).map((r) => r.id)).toEqual(['gamma']);
      expect(s.agents.every((a) => a.off && a.op === 0)).toBe(true);
      expect(s.skills.every((k) => k.off)).toBe(true);
      expect(s.wfs.every((w) => w.off)).toBe(true);
      expect(s.spokes).toHaveLength(1);
      expect(s.slots).toHaveLength(2);
    });

    it('keeps the selected repo, its workflows and the repos related to a pick', () => {
      const s = buildScene({ ...base, pick: { kind: 'a', repoId: 'alpha', name: 'a1' } });
      expect(s.repos.filter((r) => !r.off).map((r) => r.id)).toEqual(['alpha', 'beta']);
      expect(s.agents.filter((a) => !a.off).map((a) => a.key)).toEqual(['aalphaa1', 'aalphaa2', 'abetaa1']);
      expect(s.wfs.map((w) => w.off)).toEqual([false, true]);
    });

    it('grows the picked element and greys out the other elements of the repo', () => {
      const s = buildScene({ ...base, pick: { kind: 'a', repoId: 'alpha', name: 'a1' } });
      const a1 = s.agents.find((a) => a.key === 'aalphaa1')!;
      const a2 = s.agents.find((a) => a.key === 'aalphaa2')!;
      expect(a1.sz).toBeGreaterThan(a2.sz);
      expect(a1.op).toBe(1);
      expect(a2.op).toBeLessThan(0.4);
      expect(s.skills[0].op).toBeLessThan(0.4);
    });

    it('hides nothing while no repo is selected', () => {
      const s = buildScene({ ...base, selId: null });
      expect([...s.repos, ...s.agents, ...s.skills, ...s.wfs].some((n) => n.off)).toBe(false);
    });
  });

  it('lights up workflows of the selected repo', () => {
    const s = buildScene(base);
    expect(s.wfs.find((w) => w.id === 'W1')!.color).toBe('#4dd6ff');
    expect(s.wfs.find((w) => w.id === 'W2')!.color).toBe('#4dd6ff80');
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

  describe('a picked workflow', () => {
    const ref = (repoId: string, agents: string[], skills: string[]) => ({ repoId, path: 'p', linked: true, agents, skills });
    const wf: WorkflowInfo = {
      id: 'W9', name: 'ship', description: 'd', when: 'w', agents: ['A1'], skills: ['s1'],
      repos: [ref('alpha', ['A1'], ['s1']), ref('beta', [], [])], issues: []
    };
    const pickWf = (w: WorkflowInfo = wf) => buildScene({ ...base, workflows: [...wfs, w], selId: null, pick: { kind: 'w', name: 'W9' } });
    const lit = (nodes: { op: number; repoId: string; name: string }[]) => nodes.filter((n) => n.op === 1).map((n) => n.repoId + '/' + n.name);

    it('draws lines to its repos and to the agents and skills each repo copy names', () => {
      const s = pickWf();
      expect(lit(s.agents)).toEqual(['alpha/a1']); // case-insensitive; a2 is not named, beta names nothing
      expect(lit(s.skills)).toEqual(['alpha/s1']);
      expect(s.lines).toHaveLength(4); // 2 repos + 1 agent + 1 skill
      expect(s.lines.filter((l) => l.color === '#c6ff3d')).toHaveLength(1);
      expect(s.lines.filter((l) => l.color === '#a99bff')).toHaveLength(1);
    });

    it('never reaches into another repo: a name listed by one repo does not light the same name in a repo that did not list it', () => {
      // both repos have an agent a1, but only beta's copy of the workflow names it
      const s = pickWf({ ...wf, agents: [], skills: [], repos: [ref('alpha', [], []), ref('beta', ['a1'], [])] });
      expect(lit(s.agents)).toEqual(['beta/a1']);
    });

    it('falls back to the merged lists for scans without per-repo names', () => {
      const s = pickWf({ ...wf, repos: [{ repoId: 'alpha', path: 'p', linked: true }, { repoId: 'beta', path: 'p', linked: true }] });
      expect(lit(s.agents)).toEqual(['alpha/a1', 'beta/a1']);
    });

    it('lists the skills in the popover', () => {
      expect(pickWf().pop?.rows.find((r) => r.k === 'SKILLE')!.v).toBe('s1');
    });

    it('does nothing extra for workflows without skills', () => {
      const s = buildScene({ ...base, pick: { kind: 'w', name: 'W1' } });
      expect(s.pop?.rows.some((r) => r.k === 'SKILLE')).toBe(false);
    });
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
