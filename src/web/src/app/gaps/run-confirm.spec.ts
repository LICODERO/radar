import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { ApiError, RadarApi } from '../core/radar-api';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { RunConfirm } from './run-confirm';

const item = { repoId: 'terraform', repoName: 'terraform', initials: 'TE', type: 'no-claude-md', dir: '/p/terraform', prompt: 'make it' } as const;

async function open() {
  const api = { run: vi.fn(async (_r: string, _t: string, _tool: string) => ({ launched: true, toolFound: true })) };
  TestBed.configureTestingModule({ providers: [provideHttpClient(), { provide: RadarApi, useValue: api }] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.tools.set({ platform: 'macos', terminal: 'iTerm2', shell: 'posix', canLaunch: true, tools: { claude: true, codex: true }, toolPaths: { claude: '/n/claude' } });
  store.askRun(item, 'claude');
  const fixture = TestBed.createComponent(RunConfirm);
  await fixture.whenStable();
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const settle = async () => { await new Promise((r) => setTimeout(r)); fixture.detectChanges(); await fixture.whenStable(); };
  const button = (label: string) => Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === label);
  return { api, store, el, settle, button };
}

describe('RunConfirm', () => {
  it('is the only dialog: it names the tool, repo, gap and terminal and shows the command to be typed', async () => {
    const { el } = await open();
    expect(el.textContent).toContain('RUN IN ITERM2?');
    expect(el.textContent).toContain('Claude Code in terraform');
    expect(el.textContent).toContain("cd '/p/terraform' && '/n/claude' 'make it'");
    expect(el.textContent).toContain('A new iTerm2 window opens');
  });

  it('runs only on RUN and then goes away', async () => {
    const { api, store, button, settle } = await open();
    expect(api.run).not.toHaveBeenCalled();
    button('RUN')!.click();
    await settle();
    expect(api.run).toHaveBeenCalledWith('terraform', 'no-claude-md', 'claude');
    expect(store.pendingRun()).toBeNull();
  });

  it('CANCEL and a click on the backdrop drop it without running', async () => {
    const first = await open();
    first.button('CANCEL')!.click();
    expect(first.store.pendingRun()).toBeNull();
    expect(first.api.run).not.toHaveBeenCalled();
    TestBed.resetTestingModule();

    const second = await open();
    (second.el.querySelector('.backdrop') as HTMLElement).click();
    expect(second.store.pendingRun()).toBeNull();
  });

  it('shows the refusal and stays open so it can be retried or cancelled', async () => {
    const { api, store, el, button, settle } = await open();
    api.run.mockRejectedValueOnce(new ApiError('Could not open the terminal: boom', 500));
    button('RUN')!.click();
    await settle();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Could not open the terminal: boom');
    expect(store.pendingRun()).not.toBeNull();
  });

  it('warns when the tool was not found anywhere', async () => {
    const { store, el, settle } = await open();
    store.tools.set({ platform: 'macos', shell: 'posix', canLaunch: true, tools: { claude: false } });
    store.askRun(item, 'claude');
    await settle();
    expect(el.textContent).toContain('was not found');
  });
});
