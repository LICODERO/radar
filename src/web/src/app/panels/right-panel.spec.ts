import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { ScanResult } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { RightPanel } from './right-panel';

const repo = (quality: { score: number | null; findings: unknown[] } | null, claudeMd = true) => ({
  id: 'app', name: 'app', initials: 'AP', stack: '.NET', agents: [], skills: [], gaps: [],
  coverage: { score: 85, parts: { claudeMd: 40, agents: 0, skills: 0 } }, claudeMd: { exists: claudeMd, path: 'CLAUDE.md' }, quality
});

function open(r: ReturnType<typeof repo>) {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.mode.set('api');
  store.result.set({ repos: [r], workflows: [], gaps: [], summary: { repos: 1, agents: 0, skills: 0, gaps: 0 } } as unknown as ScanResult);
  store.selectRepo('app');
  const fixture = TestBed.createComponent(RightPanel);
  fixture.detectChanges();
  return { store, el: fixture.nativeElement as HTMLElement };
}

describe('RightPanel selected-repo card', () => {
  it('shows the coverage and a clickable quality chip instead of the stack name', () => {
    const { store, el } = open(repo({ score: 78, findings: [{ path: 'CLAUDE.md', code: 'x', severity: 'warning' }], files: [{ path: 'CLAUDE.md', kind: 'claude-md', score: 78 }] } as never));
    const head = el.querySelector('.cm')!;
    expect(head.textContent).not.toContain('.NET');
    expect(head.textContent?.replace(/\s+/g, '')).toBe('85%78%');
    const chip = el.querySelector<HTMLButtonElement>('.qchip')!;
    expect(chip.querySelector('svg')).not.toBeNull();
    expect(chip.getAttribute('data-tip')).toContain('FILE QUALITY 78% · 1');
    chip.click();
    expect(store.qualityOpen()).toBe(true);
  });

  it('has no quality chip and no progress bar when there is nothing to rate', () => {
    const { el } = open(repo({ score: null, findings: [] }));
    expect(el.querySelector('.qchip')).toBeNull();
    expect(el.querySelector('.qtrack, [role=progressbar]')).toBeNull();
  });

  it('opens CLAUDE.md and the local instructions from icon buttons', () => {
    const { store, el } = open(repo({ score: 90, findings: [] }));
    const [claude, local] = Array.from(el.querySelectorAll<HTMLButtonElement>('.openrow .ib'));
    expect(claude.getAttribute('aria-label')).toBe('OPEN CLAUDE.md');
    expect(claude.textContent?.trim()).toBe('');
    expect(local.getAttribute('aria-label')).toBe('LOCAL INSTRUCTIONS');
    local.click();
    expect(store.localFileRepoId()).toBe('app');
  });

  it('offers only the local instructions when there is no CLAUDE.md', () => {
    const { el } = open(repo({ score: null, findings: [] }, false));
    expect(el.querySelectorAll('.openrow .ib').length).toBe(1);
  });
});
