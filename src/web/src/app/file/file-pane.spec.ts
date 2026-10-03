import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { ScanResult } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { FilePane } from './file-pane';

const repo = {
  id: 'shop', name: 'shop', initials: 'SH', stack: '.NET', coverage: { score: 70 }, claudeMd: { exists: true, path: 'CLAUDE.md' }, gaps: [],
  agents: [{ name: 'reviewer', description: 'r', tools: [], path: '.claude/agents/reviewer.md' }],
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
    expect(store.copyAgentRef()).toEqual({ repoId: 'shop', name: 'reviewer' });
  });

  it('is not offered for a skill, CLAUDE.md or without the server', () => {
    expect(open('.claude/skills/pdf/SKILL.md').el.querySelector('.copy')).toBeNull();
    TestBed.resetTestingModule();
    expect(open('CLAUDE.md').el.querySelector('.copy')).toBeNull();
    TestBed.resetTestingModule();
    expect(open('.claude/agents/reviewer.md', 'mock').el.querySelector('.copy')).toBeNull();
  });
});
