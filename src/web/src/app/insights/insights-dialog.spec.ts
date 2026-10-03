import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { ScanChanges, ScanResult, SharedItem } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { InsightsDialog } from './insights-dialog';

const changes: ScanChanges = {
  previousScannedAt: '2026-10-01T09:00:00Z', scannedAt: '2026-10-02T09:00:00Z',
  avgCoverageBefore: 50, avgCoverageAfter: 70, avgQualityBefore: 60, avgQualityAfter: 55,
  newRepos: ['fresh'], removedRepos: [],
  changed: [{ repoId: 'shop', name: 'shop', coverageBefore: 40, coverageAfter: 70, qualityBefore: null, qualityAfter: 80, agentsDelta: 1, skillsDelta: 0 }],
  fixed: [{ repoId: 'shop', repoName: 'shop', path: 'CLAUDE.md', code: 'claude-md-thin', severity: 'warning', detail: '9' }],
  introduced: [{ repoId: 'crm', repoName: 'crm', path: '.claude/agents/a.md', code: 'not-known-yet', severity: 'warning' }]
};

const shared: SharedItem[] = [
  { kind: 'agent', name: 'reviewer', missing: ['crm'], variants: [{ hash: 'a', repos: ['shop', 'erp'], path: '.claude/agents/reviewer.md' }, { hash: 'b', repos: ['hr'], path: '.claude/agents/reviewer.md' }] },
  { kind: 'skill', name: 'pdf', missing: [], variants: [{ hash: 'c', repos: ['shop', 'hr'], path: '.claude/skills/pdf/SKILL.md' }] }
];

async function open(tab: 'changes' | 'shared', withChanges = true) {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.result.set({ repos: [], workflows: [], gaps: [], shared } as unknown as ScanResult);
  store.changes.set(withChanges ? changes : null);
  store.openInsights(tab);
  const fixture = TestBed.createComponent(InsightsDialog);
  await fixture.whenStable();
  fixture.detectChanges();
  return { store, el: fixture.nativeElement as HTMLElement };
}

describe('InsightsDialog', () => {
  it('shows what was fixed, what is new and how the averages moved', async () => {
    const { el } = await open('changes');
    const text = el.textContent ?? '';
    expect(text).toContain('50% → 70%');
    expect(text).toContain('60% → 55%');
    expect(text).toContain('FIXED · 1');
    expect(text).toContain('only 9 non-empty lines');
    expect(text).toContain('NEW FINDINGS · 1');
    expect(text).toContain('not-known-yet');          // a code from a newer server is shown as it is
    expect(text).toContain('coverage 40% → 70%');
    expect(text).toContain('agents +1');
    expect(text).toContain('NEW REPOS · 1');
  });

  it('explains the missing baseline instead of showing an empty list', async () => {
    const { el } = await open('changes', false);
    expect(el.querySelector('.none')?.textContent).toContain('No baseline yet');
  });

  it('lists drifted copies first, with their versions and the repos that lack the agent', async () => {
    const { el } = await open('shared');
    const names = [...el.querySelectorAll('.iname')].map((e) => e.textContent?.trim());
    expect(names).toEqual(['◆ reviewer', '● pdf']);
    const first = el.querySelector('.item')!;
    expect(first.textContent).toContain('3 repos · 2 versions');
    expect(first.querySelectorAll('.vrow').length).toBe(2);
    expect(first.textContent).toContain('shop, erp');
    expect(first.textContent).toContain('missing in repos of the same stack: crm');
    expect(el.querySelectorAll('.item')[1].textContent).toContain('identical');
    expect(el.querySelectorAll('.item')[1].querySelectorAll('.vrow').length).toBe(0);
  });

  it('opens the file of a version in its first repo and closes itself', async () => {
    const { store, el } = await open('shared');
    el.querySelector<HTMLButtonElement>('.vrow .open')!.click();
    expect(store.insightsTab()).toBeNull();
    expect(store.file()?.repoId).toBe('shop');
    expect(store.file()?.path).toBe('.claude/agents/reviewer.md');
  });
});
