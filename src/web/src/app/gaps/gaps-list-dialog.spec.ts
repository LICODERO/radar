import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { ScanResult } from '../core/models';
import { RadarApi } from '../core/radar-api';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { GapsListDialog } from './gaps-list-dialog';

const repo = (id: string, gaps: string[]) => ({ id, name: id, initials: id.slice(0, 2).toUpperCase(), stack: '', agents: [], skills: [], coverage: { score: 0 }, claudeMd: { exists: !gaps.includes('no-claude-md'), path: 'CLAUDE.md' }, gaps });

async function open(repos: ReturnType<typeof repo>[]) {
  const api = { gaps: vi.fn(async () => []) };
  TestBed.configureTestingModule({ providers: [provideHttpClient(), { provide: RadarApi, useValue: api }] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.result.set({
    repos, workflows: [],
    gaps: repos.flatMap((r) => r.gaps.filter((g) => g === 'no-claude-md').map((type) => ({ repoId: r.id, type })))
  } as unknown as ScanResult);
  store.openGapsList();
  const fixture = TestBed.createComponent(GapsListDialog);
  await fixture.whenStable();
  fixture.detectChanges();
  return { store, api, el: fixture.nativeElement as HTMLElement, fixture };
}

describe('gaps list', () => {
  it('opens only when there is something to list', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient()] });
    const store = TestBed.inject(RadarStore);
    store.result.set({ repos: [repo('a', [])], workflows: [], gaps: [] } as unknown as ScanResult);
    store.openGapsList();
    expect(store.gapsListOpen()).toBe(false);
  });

  it('lists the repos without CLAUDE.md with the count in the title', async () => {
    const { el } = await open([repo('shared-ui-kit', ['no-claude-md']), repo('terraform', ['no-claude-md']), repo('ok-repo', [])]);
    expect(el.textContent).toContain('GAPS TO FILL · 2');
    expect(el.querySelectorAll('.row').length).toBe(2);
    expect(el.textContent).toContain('shared-ui-kit');
    expect(el.textContent).toContain('no CLAUDE.md');
    expect(el.textContent).not.toContain('ok-repo');
  });

  it('selecting a repo closes the list and selects it', async () => {
    const { store, el } = await open([repo('shared-ui-kit', ['no-claude-md']), repo('terraform', ['no-claude-md'])]);
    (el.querySelectorAll('.row')[1] as HTMLButtonElement).click();
    expect(store.gapsListOpen()).toBe(false);
    expect(store.selected()?.id).toBe('terraform');
  });

  it('can go on to the command generator', async () => {
    const { store, el } = await open([repo('a', ['no-claude-md'])]);
    (el.querySelector('.gen') as HTMLButtonElement).click();
    expect(store.gapsListOpen()).toBe(false);
    expect(store.gapsOpen()).toBe(true);
  });
});
