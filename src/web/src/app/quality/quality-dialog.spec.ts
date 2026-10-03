import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { ScanResult } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { QualityDialog } from './quality-dialog';

const repo = {
  id: 'shop', name: 'shop', initials: 'SH', stack: '.NET', agents: [], skills: [], coverage: { score: 70 }, claudeMd: { exists: true, path: 'CLAUDE.md' },
  gaps: ['weak-files'],
  quality: {
    score: 62,
    files: [
      { path: 'CLAUDE.md', kind: 'claude-md', score: 85 },
      { path: '.claude/agents/tester.md', kind: 'agent', score: 50 },
      { path: '.claude/agents/ok.md', kind: 'agent', score: 100 }
    ],
    findings: [
      { path: 'CLAUDE.md', code: 'claude-md-broken-ref', severity: 'warning', detail: 'docs/gone.md' },
      { path: '.claude/agents/tester.md', code: 'agent-no-tools', severity: 'info' },
      { path: '.claude/agents/tester.md', code: 'agent-no-description', severity: 'error' },
      { path: '.claude/agents/tester.md', code: 'from-the-future', severity: 'warning' }
    ]
  }
};

async function open() {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.result.set({ repos: [repo], workflows: [], gaps: [] } as unknown as ScanResult);
  store.selectRepo('shop');
  store.openQuality();
  const fixture = TestBed.createComponent(QualityDialog);
  await fixture.whenStable();
  fixture.detectChanges();
  return { store, el: fixture.nativeElement as HTMLElement };
}

describe('QualityDialog', () => {
  it('lists only files with findings, the worst first, errors before minor notes', async () => {
    const { el } = await open();
    const paths = [...el.querySelectorAll('.path')].map((e) => e.textContent?.trim());
    expect(paths).toEqual(['.claude/agents/tester.md', 'CLAUDE.md']);
    const first = [...el.querySelectorAll('.file')[0].querySelectorAll('.find')].map((e) => e.className.replace('find ', ''));
    expect(first).toEqual(['error', 'warning', 'info']);
    expect(el.textContent).toContain('refers to files that do not exist: docs/gone.md');
  });

  it('shows an unknown finding code as it is instead of failing', async () => {
    const { el } = await open();
    expect(el.textContent).toContain('from-the-future');
  });

  it('does not open without scored files', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient()] });
    const store = TestBed.inject(RadarStore);
    store.result.set({ repos: [{ ...repo, quality: { score: null, files: [], findings: [] } }], workflows: [], gaps: [] } as unknown as ScanResult);
    store.selectRepo('shop');
    store.openQuality();
    expect(store.qualityOpen()).toBe(false);
  });
});
