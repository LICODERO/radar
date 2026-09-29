import { describe, expect, it } from 'vitest';
import { GapType } from './models';
import { GapItem, buildCommand, countByType, filterItems, quote, toScript } from './commands';

const item = (repo: string, type: GapType, dir = `/p/${repo}`): GapItem =>
  ({ repoId: repo, repoName: repo, initials: 'XX', type, dir, prompt: 'Do the thing.' });

const items = [item('billing', 'no-claude-md'), item('billing', 'no-agents'), item('orders', 'no-skills')];

describe('quote', () => {
  it('escapes single quotes per shell', () => {
    expect(quote('posix', `it's`)).toBe(`'it'\\''s'`);
    expect(quote('powershell', `it's`)).toBe(`'it''s'`);
  });

  it('keeps shell metacharacters inert', () => {
    expect(quote('posix', '$(rm -rf /) `x` ; &&')).toBe(`'$(rm -rf /) \`x\` ; &&'`);
    expect(quote('powershell', '$(Remove-Item x) `n; &')).toBe(`'$(Remove-Item x) \`n; &'`);
  });
});

describe('buildCommand', () => {
  it('builds a POSIX command', () => {
    expect(buildCommand('posix', 'claude', '/p/repo', 'do it')).toBe(`cd '/p/repo' && claude 'do it'`);
    expect(buildCommand('posix', 'codex', '/p/my repo', 'do it')).toBe(`cd '/p/my repo' && codex 'do it'`);
  });

  it('builds a PowerShell command that works without &&', () => {
    const c = buildCommand('powershell', 'claude', `C:\\Users\\O'Brien\\repo`, `it's fine`);
    expect(c).toBe(`Set-Location -LiteralPath 'C:\\Users\\O''Brien\\repo'; claude 'it''s fine'`);
    expect(c).not.toContain('&&');
  });
});

describe('filterItems / countByType', () => {
  it('filters by selected gap types', () => {
    expect(filterItems(items, new Set<GapType>(['no-agents'])).map((i) => i.repoId)).toEqual(['billing']);
    expect(filterItems(items, new Set<GapType>())).toEqual([]);
  });

  it('counts per type', () => {
    expect(countByType(items)).toEqual({ 'no-claude-md': 1, 'no-agents': 1, 'no-skills': 1, 'no-outputs': 0, 'workflow-not-linked': 0 });
  });
});

describe('toScript', () => {
  it('joins commands with descriptive comments', () => {
    const s = toScript('posix', 'claude', items.slice(0, 1));
    expect(s.startsWith('# billing · BRAK CLAUDE.md\ncd ')).toBe(true);
    expect(s).toContain(`&& claude '`);
  });

  it('uses PowerShell syntax when asked to', () => {
    expect(toScript('powershell', 'codex', items.slice(0, 1))).toContain('Set-Location -LiteralPath');
  });
});
