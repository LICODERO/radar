import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { ScanResult } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { CoverageInfo } from './coverage-info';

function open(parts: { claudeMd: number; agents: number; skills: number } | null) {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  const repo = parts && { id: 'app', name: 'app', initials: 'AP', stack: '.NET', agents: [], skills: [], gaps: [], coverage: { score: parts.claudeMd + parts.agents + parts.skills, parts }, claudeMd: { exists: parts.claudeMd > 0, path: 'CLAUDE.md' } };
  store.result.set({ repos: repo ? [repo] : [], workflows: [], gaps: [], summary: { avgCoverage: 60 } } as unknown as ScanResult);
  if (repo) store.selectRepo('app');
  const fixture = TestBed.createComponent(CoverageInfo);
  fixture.detectChanges();
  return (fixture.nativeElement as HTMLElement).querySelector('.sum')!.textContent!.replace(/\s+/g, '');
}

describe('CoverageInfo total', () => {
  it('shows what the selected repo really earned out of 100', () => {
    expect(open({ claudeMd: 0, agents: 30, skills: 30 })).toBe('TOTAL60/100');
  });

  it('shows the full marks as 100/100 and a repo with nothing as 0/100', () => {
    expect(open({ claudeMd: 40, agents: 30, skills: 30 })).toBe('TOTAL100/100');
    TestBed.resetTestingModule();
    expect(open({ claudeMd: 0, agents: 0, skills: 0 })).toBe('TOTAL0/100');
  });

  it('shows only the maximum when no repo is selected', () => {
    expect(open(null)).toBe('TOTAL–/100');
  });
});
