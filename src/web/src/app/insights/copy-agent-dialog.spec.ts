import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { CopyAgentResult, ScanResult } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { CopyAgentDialog } from './copy-agent-dialog';

const agent = (name: string) => ({ name, description: name, tools: [], path: `.claude/agents/${name}.md` });
const repo = (id: string, stack: string, agents: string[]) => ({ id, name: id, initials: 'XX', stack, agents: agents.map(agent), skills: [], coverage: { score: 50 }, claudeMd: { exists: true, path: 'CLAUDE.md' }, gaps: [] });

const repos = [
  repo('a-api', '.NET', ['reviewer']),
  repo('z-front', 'Angular', []),
  repo('b-api', '.NET', []),
  repo('c-api', '.NET', ['Reviewer']),   // already has it (name compared ignoring case)
  repo('d-api', '.NET', [])
];

const plan = (ids: string[]): CopyAgentResult => ({
  name: 'reviewer', source: '.claude/agents/reviewer.md', written: false,
  targets: ids.map((repoId) => ({ repoId, status: repoId === 'd-api' ? 'exists' as const : 'ready' as const, path: '.claude/agents/reviewer.md' }))
});

async function open() {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.result.set({ repos, workflows: [], gaps: [] } as unknown as ScanResult);
  const planSpy = vi.spyOn(store, 'planCopyAgent').mockImplementation(async (_f, _n, to) => plan(to));
  const applySpy = vi.spyOn(store, 'applyCopyAgent').mockResolvedValue({ ...plan([]), written: true, targets: [{ repoId: 'b-api', status: 'created', path: 'x' }] });
  store.openCopyAgent('a-api', 'reviewer');
  const fixture = TestBed.createComponent(CopyAgentDialog);
  await fixture.whenStable();
  fixture.detectChanges();
  return { store, planSpy, applySpy, fixture, el: fixture.nativeElement as HTMLElement };
}

describe('CopyAgentDialog', () => {
  it('asks about every repo that lacks the agent, the source stack first, and ticks nothing by itself', async () => {
    const { el, planSpy } = await open();
    expect(planSpy).toHaveBeenCalledWith('a-api', 'reviewer', ['b-api', 'd-api', 'z-front'], 'private');
    expect([...el.querySelectorAll('.target .rn')].map((e) => e.textContent)).toEqual(['b-api', 'z-front']);   // d-api is reported as taken
    expect([...el.querySelectorAll<HTMLInputElement>('.target input')].some((i) => i.checked)).toBe(false);
    expect(el.textContent).toContain('d-api');
    expect(el.querySelector<HTMLButtonElement>('.go')!.disabled).toBe(true);
  });

  it('writes only what is ticked and reports the outcome', async () => {
    const { el, applySpy, fixture } = await open();
    el.querySelectorAll<HTMLInputElement>('.target input')[1].click();      // z-front
    fixture.detectChanges();
    expect(el.querySelector<HTMLButtonElement>('.go')!.textContent).toContain('COPY TO 1');
    el.querySelector<HTMLButtonElement>('.go')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(applySpy).toHaveBeenCalledWith('a-api', 'reviewer', ['z-front'], 'private');
    expect(el.textContent).toContain('Copied to 1 repository.');
  });

  it('copies privately by default and plans again when the user picks public', async () => {
    const { el, planSpy, applySpy, fixture } = await open();
    expect(el.querySelector('app-visibility-picker .opt.on')?.textContent).toContain('PRIVATE');
    expect(el.textContent).toContain('The copies are private');

    el.querySelectorAll<HTMLButtonElement>('app-visibility-picker .opt')[1].click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(planSpy).toHaveBeenLastCalledWith('a-api', 'reviewer', ['b-api', 'd-api', 'z-front'], 'public');
    el.querySelectorAll<HTMLInputElement>('.target input')[0].click();
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('.go')!.click();
    await fixture.whenStable();
    expect(applySpy).toHaveBeenCalledWith('a-api', 'reviewer', ['b-api'], 'public');
  });

  it('names the repos where a private copy cannot be hidden and does not offer them', async () => {
    const { el, planSpy, fixture } = await open();
    planSpy.mockResolvedValue({ ...plan(['b-api', 'z-front']), targets: [{ repoId: 'b-api', status: 'ready' }, { repoId: 'z-front', status: 'cannot-hide' }] });
    el.querySelectorAll<HTMLButtonElement>('app-visibility-picker .opt')[1].click();
    el.querySelectorAll<HTMLButtonElement>('app-visibility-picker .opt')[0].click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect([...el.querySelectorAll('.target .rn')].map((e) => e.textContent)).toEqual(['b-api']);
    expect(el.textContent).toContain('cannot be hidden from git there');
    expect(el.textContent).toContain('z-front');
  });

  it('says so when every other repo already has the agent', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient()] });
    TestBed.inject(I18n).setLang('en');
    const store = TestBed.inject(RadarStore);
    store.result.set({ repos: [repo('a-api', '.NET', ['reviewer']), repo('b-api', '.NET', ['reviewer'])], workflows: [], gaps: [] } as unknown as ScanResult);
    const planSpy = vi.spyOn(store, 'planCopyAgent');
    store.openCopyAgent('a-api', 'reviewer');
    const fixture = TestBed.createComponent(CopyAgentDialog);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(planSpy).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Every other repository already has');
  });
});
