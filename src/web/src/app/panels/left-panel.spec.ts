import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { ScanResult } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { LeftPanel } from './left-panel';

const repo = { id: 'api', name: 'api', initials: 'AP', stack: '.NET', agents: [], skills: [], coverage: { score: 50 }, claudeMd: { exists: true, path: 'CLAUDE.md' }, gaps: [] };
const workflow = { id: 'W1', name: 'pr-review', description: 'Reviews', when: '', agents: [], repos: [{ repoId: 'api', path: 'x', linked: true }], issues: [] };

async function render(workflows: unknown[]) {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  TestBed.inject(RadarStore).result.set({ repos: [repo], workflows, gaps: [] } as unknown as ScanResult);
  const fixture = TestBed.createComponent(LeftPanel);
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('LeftPanel workflows section', () => {
  it('is hidden completely when the scan found no workflows', async () => {
    const el = await render([]);
    expect(el.textContent).toContain('REPOSITORIES');
    expect(el.textContent).not.toContain('WORKFLOWS');
    expect(el.querySelector('.wf-pager')).toBeNull();
    expect(el.querySelectorAll('.wf-row').length).toBe(0);
  });

  it('shows its heading, list and pager when there are workflows', async () => {
    const el = await render([workflow]);
    expect(el.textContent).toContain('WORKFLOWS · 1');
    expect(el.querySelectorAll('.wf-row').length).toBe(1);
    expect(el.querySelector('.wf-pager')).not.toBeNull();
  });
});
