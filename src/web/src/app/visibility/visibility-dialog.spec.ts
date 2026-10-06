import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { Visibility, VisibilityOutcome } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { VisibilityDialog } from './visibility-dialog';

const outcome = (over: Partial<VisibilityOutcome> = {}): VisibilityOutcome => ({
  path: '.claude/agents/a.md', current: 'untracked', target: 'private', changes: [{ kind: 'exclude-add', text: '/.claude/agents/a.md' }],
  blocked: null, notes: [], applied: false, visibility: 'untracked', ...over
});

async function open(current: Visibility, plan: VisibilityOutcome, applied?: VisibilityOutcome) {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  const planSpy = vi.spyOn(store, 'planVisibility').mockResolvedValue(plan);
  const applySpy = vi.spyOn(store, 'applyVisibility').mockResolvedValue(applied ?? { ...plan, applied: true, visibility: 'private' });
  store.openVisibility('app', '.claude/agents/a.md', current);
  const fixture = TestBed.createComponent(VisibilityDialog);
  await fixture.whenStable();
  fixture.detectChanges();
  return { store, planSpy, applySpy, fixture, el: fixture.nativeElement as HTMLElement };
}

const button = (el: HTMLElement, text: string) => Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes(text)) as HTMLButtonElement | undefined;

describe('VisibilityDialog', () => {
  it('plans hiding an untracked item first and shows exactly what will be written', async () => {
    const { el, planSpy } = await open('untracked', outcome());
    expect(planSpy).toHaveBeenCalledWith('app', '.claude/agents/a.md', 'private');
    expect(el.textContent).toContain('I will add an entry to .git/info/exclude');
    expect(el.querySelector('code')?.textContent).toBe('/.claude/agents/a.md');
    expect(button(el, 'HIDE')!.disabled).toBe(false);
  });

  it('asks to share a private item, and warns about the commit it needs', async () => {
    const { el, planSpy } = await open('private', outcome({ current: 'private', target: 'public', changes: [{ kind: 'exclude-remove', text: '/x' }], notes: ['commit-to-share'] }));
    expect(planSpy).toHaveBeenCalledWith('app', '.claude/agents/a.md', 'public');
    expect(button(el, 'SHARE')).toBeTruthy();
    expect(el.textContent).toContain('R.A.D.A.R. commits nothing');
  });

  it('warns that a tracked file taken out of the index leaves the repo for everyone after the commit', async () => {
    const { el } = await open('public', outcome({ current: 'public', changes: [{ kind: 'exclude-add', text: '/a' }, { kind: 'git-rm-cached', text: 'git rm --cached -- a' }], notes: ['commit-removal'] }));
    expect(el.textContent).toContain('disappears from the repo for everyone who pulls');
    expect(el.querySelectorAll('code').length).toBe(2);
  });

  it('writes only after the confirm button, then reports it done', async () => {
    const { el, applySpy, fixture } = await open('untracked', outcome());
    expect(applySpy).not.toHaveBeenCalled();
    button(el, 'HIDE')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(applySpy).toHaveBeenCalledWith('app', '.claude/agents/a.md', 'private');
    expect(el.textContent).toContain('Done: the file is private.');
  });

  it('explains a block and offers no confirm', async () => {
    const { el } = await open('private', outcome({ current: 'private', target: 'public', changes: [], blocked: 'other-rule' }));
    expect(el.querySelector('[role=alert]')?.textContent).toContain('Another rule hides the file');
    expect(button(el, 'SHARE')!.disabled).toBe(true);
  });

  it('says there is nothing to change when the plan is empty', async () => {
    const { el } = await open('private', outcome({ current: 'private', target: 'private', changes: [] }));
    expect(el.textContent).toContain('Nothing to change');
  });
});
