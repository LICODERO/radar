import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { NameConflict, ScanResult } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { ConflictDialog } from './conflict-dialog';

const agent: NameConflict = { kind: 'agent', name: 'reviewer', items: [{ path: '.claude/agents/code-reviewer.md', visibility: 'public' }, { path: '.claude/agents/mine.md', visibility: 'private' }] };
const skill: NameConflict = { kind: 'skill', name: 'deploy', items: [{ path: '.claude/skills/deploy/SKILL.md', visibility: 'public' }, { path: '.claude/skills/deploy-local/SKILL.md', visibility: 'untracked' }] };

function open(conflicts: NameConflict[]) {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.result.set({ scanRoot: '/work', repos: [{ id: 'api', name: 'api', path: 'api', initials: 'AP', stack: '.NET', agents: [], skills: [], gaps: [], coverage: { score: 0 }, claudeMd: { exists: true, path: 'CLAUDE.md' }, nameConflicts: conflicts }], workflows: [], gaps: [] } as unknown as ScanResult);
  store.openConflicts('api');
  const fixture = TestBed.createComponent(ConflictDialog);
  fixture.detectChanges();
  return { store, fixture, el: fixture.nativeElement as HTMLElement };
}

const btn = (el: HTMLElement, text: string, nth = 0) => Array.from(el.querySelectorAll('button')).filter((b) => b.textContent?.includes(text))[nth] as HTMLButtonElement;

describe('ConflictDialog', () => {
  it('lists each conflict with its files and their visibility, and shows no command yet', () => {
    const { el } = open([agent, skill]);
    expect(el.textContent).toContain('AGENT');
    expect(el.textContent).toContain('reviewer');
    expect(el.textContent).toContain('.claude/agents/mine.md');
    expect(el.querySelectorAll('.items li').length).toBe(4);
    expect(el.querySelectorAll('app-visibility-tag .ico').length).toBe(4);
    expect(el.querySelector('.cmd')).toBeNull();
    expect(el.textContent).toContain('I never delete anything myself');
  });

  it('shows the command that removes the others once one file is chosen, with a warning for a shared one', () => {
    const { el, fixture } = open([agent]);
    btn(el, 'KEEP THIS ONE', 1).click();            // keep the private one: the shared one goes
    fixture.detectChanges();
    expect(el.querySelector('.cmd')?.textContent).toBe(`cd '/work/api'\ngit rm -- '.claude/agents/code-reviewer.md'`);
    expect(el.textContent).toContain('disappears from the repo for everyone who pulls');

    btn(el, 'KEEPING').click();                      // choosing again undoes it
    fixture.detectChanges();
    expect(el.querySelector('.cmd')).toBeNull();

    btn(el, 'KEEP THIS ONE', 0).click();             // keep the shared one: the local one goes
    fixture.detectChanges();
    expect(el.querySelector('.cmd')?.textContent).toBe(`cd '/work/api'\nrm -- '.claude/agents/mine.md'`);
    expect(el.textContent).toContain('only a local file');
  });

  it('removes a whole skill folder', () => {
    const { el, fixture } = open([skill]);
    btn(el, 'KEEP THIS ONE', 0).click();
    fixture.detectChanges();
    expect(el.querySelector('.cmd')?.textContent).toContain(`rm -r -- '.claude/skills/deploy-local'`);
  });

  it('copies the script and says so', async () => {
    const write = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: write }, configurable: true });
    const { el, fixture } = open([agent]);
    btn(el, 'KEEP THIS ONE', 0).click();
    fixture.detectChanges();
    btn(el, 'COPY COMMAND').click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(write).toHaveBeenCalledWith(`cd '/work/api'\nrm -- '.claude/agents/mine.md'`);
    expect(el.textContent).toContain('COPIED');
  });

  it('opens a file in the file pane and closes itself', () => {
    const { store, el } = open([agent]);
    const openFile = vi.spyOn(store, 'openFile').mockResolvedValue();
    btn(el, 'OPEN', 1).click();
    expect(openFile).toHaveBeenCalledWith('api', '.claude/agents/mine.md', 'AGENT', '#c6ff3d');
    expect(store.conflictRepoId()).toBeNull();
  });
});
