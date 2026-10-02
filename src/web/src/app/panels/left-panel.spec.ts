import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { ScanResult } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { LeftPanel } from './left-panel';

const repo = { id: 'api', name: 'api', initials: 'AP', stack: '.NET', agents: [], skills: [], coverage: { score: 50 }, claudeMd: { exists: true, path: 'CLAUDE.md' }, gaps: [] };

describe('LeftPanel', () => {
  it('lists the repositories and the AI tools, and has no workflows section any more', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient()] });
    TestBed.inject(I18n).setLang('en');
    TestBed.inject(RadarStore).result.set({ repos: [repo], workflows: [], gaps: [] } as unknown as ScanResult);
    const fixture = TestBed.createComponent(LeftPanel);
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('REPOSITORIES');
    expect(el.querySelector('app-ai-tools')).not.toBeNull();
    expect(el.textContent).not.toContain('WORKFLOWS');
  });
});
