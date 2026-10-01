import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { EnableProjectResult, ProjectPlan, ScanResult, VaultStatus } from '../core/models';
import { ApiError, RadarApi } from '../core/radar-api';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { ProjectDialog } from './project-dialog';

const vault: VaultStatus = { state: 'ok', path: '/data/brain', pathDisplay: '~/brain', projects: [], suggestedPath: '', suggestedPathDisplay: '' };
const plan = (extra: Partial<ProjectPlan> = {}): ProjectPlan => ({
  vaultDir: '/data/brain/api', creates: ['/data/brain/api', '/data/brain/api/raw', '/data/brain/api/_index.md'],
  claudeLocalPath: '/repos/api/CLAUDE.local.md', claudeLocalExists: false, block: '## Second brain\n\nVault: `/data/brain`', alreadyEnabled: false, gitIgnored: true, ...extra
});
const result = (applied: boolean, p: ProjectPlan): EnableProjectResult => ({ applied, plan: p, vault: { ...vault, projects: applied ? [{ name: 'api', repoId: 'api' }] : [] } });

async function open(p: ProjectPlan) {
  const api = {
    enableProject: vi.fn(async (_id: string, confirm: boolean) => result(confirm, p))
  };
  TestBed.configureTestingModule({ providers: [provideHttpClient(), { provide: RadarApi, useValue: api }] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.vault.set(vault);
  store.result.set({ repos: [{ id: 'api', name: 'api', agents: [], skills: [], coverage: { score: 0 }, gaps: [] }], workflows: [], gaps: [] } as unknown as ScanResult);
  store.selectRepo('api');
  store.openProject();
  const fixture = TestBed.createComponent(ProjectDialog);
  await fixture.whenStable();
  const el = fixture.nativeElement as HTMLElement;
  // the mocked API resolves on later ticks, which Angular does not track: wait a macrotask, then render
  const settle = async () => { await new Promise((r) => setTimeout(r)); fixture.detectChanges(); await fixture.whenStable(); };
  const button = (label: string) => Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === label);
  return { api, store, el, settle, button };
}

describe('ProjectDialog', () => {
  it('shows what would be written before anything is saved', async () => {
    const { el, api } = await open(plan());
    expect(api.enableProject).toHaveBeenCalledTimes(1);
    expect(api.enableProject).toHaveBeenCalledWith('api', false);
    // the vault part is shown relative to the vault folder
    expect(el.textContent).toContain('api/raw');
    expect(el.textContent).not.toContain('/data/brain/api/raw');
    expect(el.textContent).toContain('The file /repos/api/CLAUDE.local.md will be created.');
    expect(el.textContent).toContain('Vault: `/data/brain`');
    expect(el.textContent).toContain('git ignores CLAUDE.local.md ✓');
  });

  it('says it will edit an existing CLAUDE.local.md without touching the rest', async () => {
    const { el } = await open(plan({ claudeLocalExists: true }));
    expect(el.textContent).toContain('A block in /repos/api/CLAUDE.local.md will be added or refreshed.');
  });

  it('warns when git does not ignore CLAUDE.local.md and when it cannot tell', async () => {
    const warn = await open(plan({ gitIgnored: false }));
    expect(warn.el.querySelector('[role="alert"]')?.textContent).toContain('git does not ignore CLAUDE.local.md');
    TestBed.resetTestingModule();

    const unknown = await open(plan({ gitIgnored: undefined }));
    expect(unknown.el.textContent).toContain('Could not check whether git ignores');
    expect(unknown.el.querySelector('[role="alert"]')).toBeNull();
  });

  it('writes only after SAVE and then reports success and updates the vault status', async () => {
    const { el, api, button, settle, store } = await open(plan());
    expect(api.enableProject).not.toHaveBeenCalledWith('api', true);

    button('SAVE')!.click();
    await settle();
    await settle();

    expect(api.enableProject).toHaveBeenLastCalledWith('api', true);
    expect(el.textContent).toContain('Second brain is on');
    expect(store.selectedHasVault()).toBe(true);
  });

  it('offers a refresh instead of a first write when the repo is already enabled', async () => {
    const { el, button } = await open(plan({ alreadyEnabled: true, creates: [] }));
    expect(el.textContent).toContain('already on for this repo');
    expect(el.textContent).toContain('Nothing new');
    expect(button('REFRESH THE BLOCK')).toBeDefined();
  });

  it('keeps the plan and shows the server message when saving is refused', async () => {
    const { el, api, button, settle } = await open(plan());
    api.enableProject.mockRejectedValueOnce(new ApiError('The second brain block markers in CLAUDE.local.md are damaged.', 409));
    button('SAVE')!.click();
    await settle();
    await settle();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('markers in CLAUDE.local.md are damaged');
    expect(button('SAVE')).toBeDefined();
  });

  it('shows the server message when the preview itself fails', async () => {
    const api = { enableProject: vi.fn(async () => { throw new ApiError('The second brain is not set up.', 409); }) };
    TestBed.configureTestingModule({ providers: [provideHttpClient(), { provide: RadarApi, useValue: api }] });
    TestBed.inject(I18n).setLang('en');
    const store = TestBed.inject(RadarStore);
    store.vault.set(vault);
    store.projectRepoId.set('api');
    const fixture = TestBed.createComponent(ProjectDialog);
    await fixture.whenStable();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('The second brain is not set up.');
  });
});
