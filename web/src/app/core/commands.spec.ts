import { describe, expect, it } from 'vitest';
import { GapType, RepoInfo, ScanResult } from './models';
import { buildCommand, gapCounts, gapItems, promptFor, shq, toScript } from './commands';

const repo = (id: string, gaps: GapType[]): RepoInfo => ({
  id, name: id.split('/').pop()!, path: id, initials: 'XX', stack: '.NET', stacks: ['.NET'],
  claudeMd: { exists: !gaps.includes('no-claude-md'), path: 'CLAUDE.md' }, agents: [], skills: [],
  outputs: { exists: false, path: '', notes: 0 }, coverage: { score: 0, parts: { claudeMd: 0, agents: 0, skills: 0, outputs: 0 } }, gaps
});

const result = (): ScanResult => ({
  schemaVersion: 1, scanRoot: '/Users/me/projects', scannedAt: '', durationMs: 0,
  summary: { repos: 3, agents: 0, skills: 0, workflows: 1, gaps: 1, stacks: [], avgCoverage: 0 },
  repos: [repo('billing-api', ['no-claude-md', 'no-agents']), repo('group/orders', ['workflow-not-linked', 'no-skills']), repo('ok', [])],
  workflows: [{ id: 'W1', name: 'commit', description: '', when: '', agents: [], issues: [],
    repos: [{ repoId: 'group/orders', path: '.claude/workflows/commit.md', linked: false }] }],
  gaps: [{ repoId: 'billing-api', type: 'no-claude-md' }], warnings: []
});

describe('shq', () => {
  it('wraps in single quotes and escapes embedded quotes', () => {
    expect(shq('a b')).toBe(`'a b'`);
    expect(shq(`it's`)).toBe(`'it'\\''s'`);
  });

  it('keeps shell metacharacters inert', () => {
    expect(shq('$(rm -rf /) `x` ; &&')).toBe(`'$(rm -rf /) \`x\` ; &&'`);
  });
});

describe('buildCommand', () => {
  it('cds into the repo and starts the tool with the prompt', () => {
    expect(buildCommand('claude', '/p/repo', 'do it')).toBe(`cd '/p/repo' && claude 'do it'`);
    expect(buildCommand('codex', '/p/my repo', 'do it')).toBe(`cd '/p/my repo' && codex 'do it'`);
  });
});

describe('promptFor', () => {
  it('has a prompt for every gap type and never contains a single quote', () => {
    for (const t of ['no-claude-md', 'no-agents', 'no-skills', 'no-outputs', 'workflow-not-linked'] as GapType[]) {
      const p = promptFor(t, ['.claude/workflows/a.md']);
      expect(p.length).toBeGreaterThan(50);
      expect(p).not.toContain(`'`);
      expect(p).not.toContain('\n');
    }
  });

  it('lists the unlinked workflow files', () => {
    expect(promptFor('workflow-not-linked', ['.claude/workflows/a.md', '.claude/workflows/b.md'])).toContain('.claude/workflows/a.md, .claude/workflows/b.md');
  });
});

describe('gapCounts and gapItems', () => {
  it('counts repos per gap type', () => {
    expect(gapCounts(result())).toEqual({ 'no-claude-md': 1, 'no-agents': 1, 'no-skills': 1, 'no-outputs': 0, 'workflow-not-linked': 1 });
    expect(gapCounts(null)['no-claude-md']).toBe(0);
  });

  it('builds items only for the selected types, using absolute repo paths', () => {
    const items = gapItems(result(), new Set<GapType>(['no-claude-md']));
    expect(items).toHaveLength(1);
    expect(items[0].dir).toBe('/Users/me/projects/billing-api');
  });

  it('keeps the gap order per repo and adds workflow files to the prompt', () => {
    const items = gapItems(result(), new Set<GapType>(['no-agents', 'no-claude-md', 'no-skills', 'workflow-not-linked']));
    expect(items.map((i) => `${i.repoId}:${i.type}`)).toEqual([
      'billing-api:no-claude-md', 'billing-api:no-agents', 'group/orders:no-skills', 'group/orders:workflow-not-linked'
    ]);
    expect(items[3].prompt).toContain('.claude/workflows/commit.md');
    expect(items[3].dir).toBe('/Users/me/projects/group/orders');
  });

  it('returns nothing without a result', () => {
    expect(gapItems(null, new Set<GapType>(['no-claude-md']))).toEqual([]);
  });
});

describe('toScript', () => {
  it('joins commands with descriptive comments', () => {
    const s = toScript('claude', gapItems(result(), new Set<GapType>(['no-claude-md'])));
    expect(s.startsWith('# billing-api · BRAK CLAUDE.md\ncd ')).toBe(true);
    expect(s).toContain(`&& claude '`);
  });
});
