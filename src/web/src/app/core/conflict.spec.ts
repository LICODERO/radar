import { describe, expect, it } from 'vitest';
import { cdCommand, removalCommand, removalPath, repoDir } from './conflict';

describe('conflict commands', () => {
  it('deletes a whole skill folder, not just its SKILL.md', () => {
    expect(removalPath('skill', { path: '.claude/skills/deploy/SKILL.md', visibility: 'private' })).toBe('.claude/skills/deploy');
    expect(removalPath('agent', { path: '.claude/agents/a.md', visibility: 'private' })).toBe('.claude/agents/a.md');
  });

  it('uses git rm for a shared item and a plain delete for a local one (POSIX)', () => {
    expect(removalCommand('posix', 'agent', { path: '.claude/agents/a.md', visibility: 'public' })).toBe(`git rm -- '.claude/agents/a.md'`);
    expect(removalCommand('posix', 'agent', { path: '.claude/agents/a.md', visibility: 'private' })).toBe(`rm -- '.claude/agents/a.md'`);
    expect(removalCommand('posix', 'skill', { path: '.claude/skills/d/SKILL.md', visibility: 'untracked' })).toBe(`rm -r -- '.claude/skills/d'`);
    expect(removalCommand('posix', 'skill', { path: '.claude/skills/d/SKILL.md', visibility: 'public' })).toBe(`git rm -r -- '.claude/skills/d'`);
  });

  it('writes PowerShell for a local item', () => {
    expect(removalCommand('powershell', 'workflow', { path: '.claude/workflows/w.md', visibility: 'private' })).toBe(`Remove-Item -LiteralPath '.claude/workflows/w.md'`);
    expect(removalCommand('powershell', 'skill', { path: '.claude/skills/d/SKILL.md', visibility: 'private' })).toBe(`Remove-Item -Recurse -LiteralPath '.claude/skills/d'`);
  });

  it('quotes odd characters', () => {
    expect(removalCommand('posix', 'agent', { path: ".claude/agents/it's.md", visibility: 'private' })).toBe(`rm -- '.claude/agents/it'\\''s.md'`);
  });

  it('builds the repo folder and the cd line for each shell', () => {
    expect(repoDir('/home/me/work/', 'api/orders', 'posix')).toBe('/home/me/work/api/orders');
    expect(repoDir('C:\\work', 'api/orders', 'powershell')).toBe('C:\\work\\api\\orders');
    expect(cdCommand('posix', '/home/me/work')).toBe(`cd '/home/me/work'`);
    expect(cdCommand('powershell', 'C:\\work')).toBe(`Set-Location -LiteralPath 'C:\\work'`);
  });
});
