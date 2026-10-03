import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { ScanResult, SharedItem } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { InsightsDialog } from './insights-dialog';

const shared: SharedItem[] = [
  { kind: 'agent', name: 'reviewer', missing: ['crm'], variants: [{ hash: 'a', repos: ['shop', 'erp'], path: '.claude/agents/reviewer.md' }, { hash: 'b', repos: ['hr'], path: '.claude/agents/reviewer.md' }] },
  { kind: 'skill', name: 'pdf', missing: [], variants: [{ hash: 'c', repos: ['shop', 'hr'], path: '.claude/skills/pdf/SKILL.md' }] }
];

async function open(items: SharedItem[] = shared) {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.result.set({ repos: [], workflows: [], gaps: [], shared: items } as unknown as ScanResult);
  store.openInsights();
  const fixture = TestBed.createComponent(InsightsDialog);
  await fixture.whenStable();
  fixture.detectChanges();
  return { store, el: fixture.nativeElement as HTMLElement };
}

describe('InsightsDialog', () => {
  it('lists drifted copies first, with their versions and the repos that lack the agent', async () => {
    const { el } = await open();
    expect([...el.querySelectorAll('.iname')].map((e) => e.textContent?.trim())).toEqual(['◆ reviewer', '● pdf']);
    const first = el.querySelector('.item')!;
    expect(first.textContent).toContain('3 repos · 2 versions');
    expect(first.querySelectorAll('.vrow').length).toBe(2);
    expect(first.textContent).toContain('shop, erp');
    expect(first.textContent).toContain('missing in repos of the same stack: crm');
    expect(el.querySelectorAll('.item')[1].textContent).toContain('identical');
    expect(el.querySelectorAll('.item')[1].querySelectorAll('.vrow').length).toBe(0);
  });

  it('opens the file of a version in its first repo and closes itself', async () => {
    const { store, el } = await open();
    el.querySelector<HTMLButtonElement>('.vrow .open')!.click();
    expect(store.insightsOpen()).toBe(false);
    expect(store.file()?.repoId).toBe('shop');
    expect(store.file()?.path).toBe('.claude/agents/reviewer.md');
  });

  it('says so when nothing is shared', async () => {
    const { el } = await open([]);
    expect(el.querySelector('.none')?.textContent).toContain('No agent or skill appears');
  });
});
