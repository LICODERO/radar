import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { LocalPlan, ScanResult } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { LocalFileDialog } from './local-file-dialog';

const plan = (over: Partial<LocalPlan> = {}): LocalPlan => ({
  parts: [
    { id: 'exclude', file: '.git/info/exclude', action: 'update', text: '/CLAUDE.local.md' },
    { id: 'workflows-local', file: 'CLAUDE.local.md', action: 'create', text: '## Workflows (private)\n- `scratch`' }
  ],
  blocked: null, notes: [], publicCandidates: 0, applied: false, ...over
});

async function open(first: LocalPlan, applied?: LocalPlan) {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.mode.set('api');
  store.result.set({ repos: [{ id: 'app', name: 'app', initials: 'AP', stack: '.NET', agents: [], skills: [], coverage: { score: 0 }, gaps: [], claudeMd: { exists: true, path: 'CLAUDE.md' } }], workflows: [], gaps: [] } as unknown as ScanResult);
  const planSpy = vi.spyOn(store, 'planLocalFile').mockResolvedValue(first);
  const applySpy = vi.spyOn(store, 'applyLocalFile').mockResolvedValue(applied ?? { ...first, applied: true });
  store.selectRepo('app');
  store.openLocalFile();
  const fixture = TestBed.createComponent(LocalFileDialog);
  await fixture.whenStable();
  fixture.detectChanges();
  return { store, planSpy, applySpy, fixture, el: fixture.nativeElement as HTMLElement };
}

const go = (el: HTMLElement) => el.querySelector<HTMLButtonElement>('.go')!;

describe('LocalFileDialog', () => {
  it('shows every part of the plan with its text and writes nothing until confirmed', async () => {
    const { el, planSpy, applySpy } = await open(plan());
    expect(planSpy).toHaveBeenCalledWith('app', false);
    expect(el.textContent).toContain('Hiding CLAUDE.local.md from git');
    expect(el.textContent).toContain('Index of private workflows');
    expect(el.textContent).toContain('create · CLAUDE.local.md');
    expect(el.querySelector('pre')?.textContent).toBe('/CLAUDE.local.md');
    expect(applySpy).not.toHaveBeenCalled();
    expect(go(el).disabled).toBe(false);
  });

  it('writes on confirm and reports it in the past tense', async () => {
    const { el, applySpy, fixture } = await open(plan());
    go(el).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(applySpy).toHaveBeenCalledWith('app', false);
    expect(el.textContent).toContain('Done.');
    expect(el.textContent).toContain('created · CLAUDE.local.md');
  });

  it('says there is nothing to do and offers no save', async () => {
    const { el } = await open(plan({ parts: [] }));
    expect(el.textContent).toContain('Nothing to do');
    expect(go(el).disabled).toBe(true);
  });

  it('explains why it refuses to write', async () => {
    const { el } = await open(plan({ parts: [], blocked: 'local-tracked' }));
    expect(el.querySelector('[role=alert]')?.textContent).toContain('already tracked by git');
    expect(go(el).disabled).toBe(true);
  });

  it('offers the public workflows only when there are some, and plans again when ticked', async () => {
    const none = await open(plan());
    expect(none.el.querySelector('.pub')).toBeNull();
    TestBed.resetTestingModule();

    const { el, planSpy, fixture } = await open(plan({ publicCandidates: 2 }));
    expect(el.querySelector('.pub')).not.toBeNull();
    planSpy.mockResolvedValue(plan({ publicCandidates: 2, parts: [...plan().parts, { id: 'workflows-public', file: 'CLAUDE.md', action: 'update', text: '## Workflows' }] }));
    el.querySelector<HTMLInputElement>('.pub input')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(planSpy).toHaveBeenLastCalledWith('app', true);
    expect(el.textContent).toContain('Public workflows in CLAUDE.md');
  });

  it('points out an AGENTS.md that CLAUDE.md does not import', async () => {
    const { el } = await open(plan({ notes: ['agents-md-not-imported'] }));
    expect(el.textContent).toContain('Add the line @AGENTS.md to CLAUDE.md');
  });
});
