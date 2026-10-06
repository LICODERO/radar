import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
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
  it('shows the local instructions button and a clickable quality chip, without the stack name or the coverage text', () => {
    const { store, el } = open(repo({ score: 78, findings: [{ path: 'CLAUDE.md', code: 'x', severity: 'warning' }], files: [{ path: 'CLAUDE.md', kind: 'claude-md', score: 78 }] } as never));
    const head = el.querySelector('.cm')!;
    expect(head.textContent).not.toContain('.NET');
    expect(head.textContent?.replace(/\s+/g, '')).toBe('78%');
    const chip = el.querySelector<HTMLButtonElement>('.qchip')!;
    expect(chip.querySelector('svg')).not.toBeNull();
    expect(chip.getAttribute('data-tip')).toContain('FILE QUALITY 78% · 1');
    chip.click();
    expect(store.qualityOpen()).toBe(true);
    expect(el.querySelector('.qtrack, [role=progressbar], .openrow')).toBeNull();
  });

  it('has no quality chip when there is nothing to rate', () => {
    const { el } = open(repo({ score: null, findings: [] }));
    expect(el.querySelector('.qchip')).toBeNull();
    expect(el.querySelector('.hb')).not.toBeNull();
  });

  it('opens the local instructions from the icon button in the header', () => {
    const { store, el } = open(repo({ score: 90, findings: [] }));
    const btn = el.querySelector<HTMLButtonElement>('.cm .hb')!;
    expect(btn.getAttribute('aria-label')).toBe('LOCAL INSTRUCTIONS');
    expect(btn.textContent?.trim()).toBe('');
    btn.click();
    expect(store.localFileRepoId()).toBe('app');
  });

  it('makes CLAUDE.md a violet link that opens the file', () => {
    const { store, el } = open(repo({ score: 90, findings: [] }));
    const flags = el.querySelector<HTMLElement>('.flags')!;
    const link = flags.querySelector<HTMLButtonElement>('.clink')!;
    expect(link.textContent?.trim()).toBe('CLAUDE.md');
    expect(flags.style.color).toBe('rgb(169, 155, 255)');
    expect(flags.textContent).toContain('✓');
    const openFile = vi.spyOn(store, 'openFile').mockResolvedValue();
    link.click();
    expect(openFile).toHaveBeenCalledWith('app', 'CLAUDE.md', 'CLAUDE.md', '#c6ff3d');
  });

  it('makes a missing CLAUDE.md a red link that starts the creation flow', () => {
    const { store, el } = open(repo({ score: null, findings: [] }, false));
    const flags = el.querySelector<HTMLElement>('.flags')!;
    expect(flags.style.color).toBe('rgb(255, 79, 163)');
    expect(flags.textContent).toContain('MISSING');
    const create = vi.spyOn(store, 'createClaudeMd').mockResolvedValue();
    const link = flags.querySelector<HTMLButtonElement>('.clink')!;
    expect(link.getAttribute('data-tip')).toContain('click to create it');
    link.click();
    expect(create).toHaveBeenCalledWith('app');
  });
});
