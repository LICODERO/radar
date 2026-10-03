import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { ScanChanges } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { ChangesToast } from './changes-toast';

const base: ScanChanges = {
  previousScannedAt: '2026-10-01T09:00:00Z', scannedAt: '2026-10-02T09:00:00Z', avgCoverageBefore: 42, avgCoverageAfter: 50, avgQualityBefore: null, avgQualityAfter: null,
  newRepos: [], removedRepos: [], changed: [], fixed: [], introduced: []
};
const finding = { repoId: 'r', repoName: 'r', path: 'CLAUDE.md', code: 'claude-md-thin', severity: 'warning' as const };

function show(c: ScanChanges) {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.changesToast.set(c);
  const fixture = TestBed.createComponent(ChangesToast);
  fixture.detectChanges();
  return { store, el: fixture.nativeElement as HTMLElement };
}

describe('ChangesToast', () => {
  it('sums up what was fixed, what is new and the coverage move', () => {
    const { el } = show({ ...base, fixed: [finding, finding], introduced: [finding], newRepos: ['x'] });
    expect(el.querySelector('.txt')?.textContent).toBe('2 fixed · 1 new finding · 1 new repo · coverage 42% → 50%');
    expect(el.querySelector('.toast')?.classList.contains('bad')).toBe(true);
  });

  it('says something even when only agent or skill counts moved', () => {
    const { el } = show({ ...base, avgCoverageAfter: 42 });
    expect(el.querySelector('.txt')?.textContent).toBe('changes in agents or skills');
  });

  it('opens the details and goes away on click, and can be dismissed', () => {
    const a = show({ ...base, fixed: [finding] });
    a.el.querySelector<HTMLButtonElement>('.main')!.click();
    expect(a.store.insightsTab()).toBe('changes');
    expect(a.store.changesToast()).toBeNull();
    TestBed.resetTestingModule();
    const b = show({ ...base, fixed: [finding] });
    b.el.querySelector<HTMLButtonElement>('.x')!.click();
    expect(b.store.changesToast()).toBeNull();
    expect(b.store.insightsTab()).toBeNull();
  });
});
