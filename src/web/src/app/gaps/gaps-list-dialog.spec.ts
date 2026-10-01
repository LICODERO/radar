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
  const api = { gaps: vi.fn(async () => repos.flatMap((x) => x.gaps.filter((g) => g === 'no-claude-md').map((type) => ({ repoId: x.id, repoName: x.id, initials: x.initials, type, dir: '/p/' + x.id, prompt: 'make claude md' })))) };
  TestBed.configureTestingModule({ providers: [provideHttpClient(), { provide: RadarApi, useValue: api }] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.result.set({
    repos, workflows: [],
    gaps: repos.flatMap((r) => r.gaps.filter((g) => g === 'no-claude-md').map((type) => ({ repoId: r.id, type })))
  } as unknown as ScanResult);
  store.tools.set({ platform: 'macos', shell: 'posix', canLaunch: true, tools: { claude: true, codex: true }, toolPaths: { claude: '/n/v20/bin/claude', codex: null } });
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
    (el.querySelectorAll('.pick')[1] as HTMLButtonElement).click();
    expect(store.gapsListOpen()).toBe(false);
    expect(store.selected()?.id).toBe('terraform');
  });

  it('has a CREATE button for every repo next to its name', async () => {
    const { el } = await open([repo('shared-ui-kit', ['no-claude-md']), repo('terraform', ['no-claude-md'])]);
    const buttons = Array.from(el.querySelectorAll('.create'));
    expect(buttons.length).toBe(2);
    expect(buttons[0].textContent?.trim()).toBe('CREATE');
  });

  it('CREATE asks for confirmation to run the CLAUDE.md prompt for that repo; it does not run anything by itself', async () => {
    const { store, el, api } = await open([repo('shared-ui-kit', ['no-claude-md']), repo('terraform', ['no-claude-md'])]);
    (el.querySelectorAll('.create')[1] as HTMLButtonElement).click();
    await vi.waitFor(() => expect(store.pendingRun()).not.toBeNull());
    expect(store.gapsListOpen()).toBe(false);
    expect(store.gapsOpen()).toBe(false); // just the confirmation, not the command panel behind it
    const p = store.pendingRun()!;
    expect(p.item.repoId).toBe('terraform');
    expect(p.item.type).toBe('no-claude-md');
    expect(p.command).toContain("cd '/p/terraform' && '/n/v20/bin/claude' 'make claude md'"); // by full path: the terminal's PATH may lack it
    expect(api.gaps).toHaveBeenCalled();
  });

  it('CREATE shows the commands to copy where no terminal can be launched', async () => {
    const { store, el } = await open([repo('a', ['no-claude-md'])]);
    store.tools.set({ platform: 'linux', shell: 'posix', canLaunch: false, tools: {} });
    (el.querySelector('.create') as HTMLButtonElement).click();
    await vi.waitFor(() => expect(store.gapsOpen()).toBe(true));
    expect(store.pendingRun()).toBeNull();
  });

  it('can go on to the command generator', async () => {
    const { store, el } = await open([repo('a', ['no-claude-md'])]);
    (el.querySelector('.gen') as HTMLButtonElement).click();
    expect(store.gapsListOpen()).toBe(false);
    expect(store.gapsOpen()).toBe(true);
  });
});
