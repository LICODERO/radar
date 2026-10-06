import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { ScanResult } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { FilePane } from './file-pane';

const repo = {
  id: 'shop', name: 'shop', initials: 'SH', stack: '.NET', coverage: { score: 70 }, claudeMd: { exists: true, path: 'CLAUDE.md' }, gaps: [],
  agents: [{ name: 'reviewer', description: 'r', tools: [], path: '.claude/agents/reviewer.md', visibility: 'private' }],
  skills: [{ name: 'pdf', description: 'p', path: '.claude/skills/pdf/SKILL.md' }]
};

function open(path: string, mode: 'api' | 'mock' = 'api') {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.mode.set(mode);
  store.result.set({ repos: [repo], workflows: [], gaps: [] } as unknown as ScanResult);
  store.file.set({ repoId: 'shop', repoName: 'shop', path, kind: 'Agent', color: '#c6ff3d', status: 'ready', text: '# x', truncated: false, bytes: 3, error: null, mode: 'preview' });
  const fixture = TestBed.createComponent(FilePane);
  fixture.detectChanges();
  return { store, el: fixture.nativeElement as HTMLElement };
}

describe('FilePane copy button', () => {
  it('is offered for an agent and opens the copy dialog for it', () => {
    const { store, el } = open('.claude/agents/reviewer.md');
    const btn = el.querySelector<HTMLButtonElement>('.copy')!;
    expect(btn.getAttribute('aria-label')).toBe('COPY TO ANOTHER REPO');
    expect(btn.querySelector('svg')).not.toBeNull();
    btn.click();
    expect(store.copyAgentRef()).toEqual({ repoId: 'shop', name: 'reviewer', kind: 'agent' });
  });

  it('is offered for a skill too and opens the copy dialog for the whole skill', () => {
    const { store, el } = open('.claude/skills/pdf/SKILL.md');
    const btn = el.querySelector<HTMLButtonElement>('.copy')!;
    expect(btn.getAttribute('aria-label')).toBe('COPY SKILL TO ANOTHER REPO');
    btn.click();
    expect(store.copyAgentRef()).toEqual({ repoId: 'shop', name: 'pdf', kind: 'skill' });
  });

  it('is not offered for CLAUDE.md or without the server', () => {
    expect(open('CLAUDE.md').el.querySelector('.copy')).toBeNull();
    TestBed.resetTestingModule();
    expect(open('.claude/agents/reviewer.md', 'mock').el.querySelector('.copy')).toBeNull();
  });
});

describe('FilePane visibility button', () => {
  it('sits next to the copy button, carries the tooltip and opens the visibility dialog', () => {
    const { store, el } = open('.claude/agents/reviewer.md');
    const btn = el.querySelector<HTMLButtonElement>('.tools .vis-b')!;
    expect(btn.nextElementSibling?.classList.contains('copy')).toBe(true);
    expect(btn.querySelector('.ico.private')).not.toBeNull();
    expect(btn.querySelector('.ico')?.getAttribute('data-tip')).toBeNull();           // the button carries the tooltip, not the icon
    expect(btn.getAttribute('data-tip')).toContain('PRIVATE – Only on your machine');
    expect(btn.getAttribute('data-tip')).toContain('Click to change.');
    btn.click();
    expect(store.visibilityRef()).toEqual({ repoId: 'shop', path: '.claude/agents/reviewer.md', current: 'private' });
  });

  it('is only an icon without the server, and absent for files git has no state for', () => {
    expect(open('.claude/agents/reviewer.md', 'mock').el.querySelector('.tools .vis-b')).toBeNull();
    TestBed.resetTestingModule();
    const { el } = open('.claude/agents/reviewer.md', 'mock');
    expect(el.querySelector('.tools .vis-s .ico')).not.toBeNull();
    TestBed.resetTestingModule();
    expect(open('CLAUDE.md').el.querySelector('.tools .vis-b, .tools .vis-s')).toBeNull();
  });
});

describe('FilePane name conflict button', () => {
  it('appears only for a file that shares its name with a file of another visibility, and opens the dialog', () => {
    expect(open('.claude/agents/reviewer.md').el.querySelector('.tools .warn')).toBeNull();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient()] });
    TestBed.inject(I18n).setLang('en');
    const store = TestBed.inject(RadarStore);
    store.mode.set('api');
    const withConflict = { ...repo, nameConflicts: [{ kind: 'agent', name: 'reviewer', items: [{ path: '.claude/agents/reviewer.md', visibility: 'private' }, { path: '.claude/agents/other.md', visibility: 'public' }] }] };
    store.result.set({ repos: [withConflict], workflows: [], gaps: [] } as unknown as ScanResult);
    store.file.set({ repoId: 'shop', repoName: 'shop', path: '.claude/agents/reviewer.md', kind: 'Agent', color: '#c6ff3d', status: 'ready', text: '# x', truncated: false, bytes: 3, error: null, mode: 'preview' });
    const fixture = TestBed.createComponent(FilePane);
    fixture.detectChanges();
    const btn = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.tools .warn')!;
    expect(btn).not.toBeNull();
    btn.click();
    expect(store.conflictRepoId()).toBe('shop');
  });
});
