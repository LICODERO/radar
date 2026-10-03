import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { CopyAgentResult, SharedItem } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { CopyAgentDialog } from './copy-agent-dialog';

const item: SharedItem = {
  kind: 'agent', name: 'reviewer', missing: ['b-api', 'c-api', 'd-api'],
  variants: [{ hash: 'a', repos: ['a-api'], path: '.claude/agents/reviewer.md' }, { hash: 'b', repos: ['z-api'], path: '.claude/agents/reviewer.md' }]
};

const plan: CopyAgentResult = {
  name: 'reviewer', source: '.claude/agents/reviewer.md', written: false,
  targets: [
    { repoId: 'b-api', status: 'ready', path: '.claude/agents/reviewer.md' },
    { repoId: 'c-api', status: 'ready', path: '.claude/agents/reviewer.md' },
    { repoId: 'd-api', status: 'exists' }
  ]
};

async function open() {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  const planSpy = vi.spyOn(store, 'planCopyAgent').mockResolvedValue(plan);
  const applySpy = vi.spyOn(store, 'applyCopyAgent').mockResolvedValue({ ...plan, written: true, targets: [{ repoId: 'c-api', status: 'created', path: 'x' }] });
  store.openCopyAgent(item);
  const fixture = TestBed.createComponent(CopyAgentDialog);
  await fixture.whenStable();
  fixture.detectChanges();
  return { store, planSpy, applySpy, fixture, el: fixture.nativeElement as HTMLElement };
}

describe('CopyAgentDialog', () => {
  it('asks the server where the agent can go and offers only those repos, all ticked, noting the skipped one', async () => {
    const { el, planSpy } = await open();
    expect(planSpy).toHaveBeenCalledWith('a-api', 'reviewer', ['b-api', 'c-api', 'd-api']);
    expect([...el.querySelectorAll('.target .rn')].map((e) => e.textContent)).toEqual(['b-api', 'c-api']);
    expect([...el.querySelectorAll<HTMLInputElement>('.target input')].every((i) => i.checked)).toBe(true);
    expect(el.textContent).toContain('already there');
    expect(el.textContent).toContain('d-api');
    expect(el.querySelector<HTMLButtonElement>('.go')!.textContent).toContain('COPY TO 2 REPOS');
  });

  it('writes only the repos that are still ticked and reports the outcome', async () => {
    const { el, applySpy, fixture } = await open();
    el.querySelectorAll<HTMLInputElement>('.target input')[0].click();      // untick b-api
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('.go')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(applySpy).toHaveBeenCalledWith('a-api', 'reviewer', ['c-api']);
    expect(el.textContent).toContain('Copied to 1 repository.');
    expect(el.textContent).toContain('c-api · created');
  });

  it('copies from the version that was picked', async () => {
    const { el, applySpy, fixture } = await open();
    el.querySelectorAll<HTMLButtonElement>('.ver')[1].click();
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('.go')!.click();
    await fixture.whenStable();
    expect(applySpy).toHaveBeenCalledWith('z-api', 'reviewer', ['b-api', 'c-api']);
  });

  it('cannot write while nothing is ticked', async () => {
    const { el, fixture } = await open();
    el.querySelectorAll<HTMLInputElement>('.target input').forEach((i) => i.click());
    fixture.detectChanges();
    expect(el.querySelector<HTMLButtonElement>('.go')!.disabled).toBe(true);
  });
});
