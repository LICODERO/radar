import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { EnableProjectResult, ProjectPlan, ScanResult, SkillStatus, VaultStatus } from '../core/models';
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

const skillStatus = (state: SkillStatus['state']): SkillStatus => ({ name: 'radar-second-brain', state, path: '/home/me/.claude/skills/radar-second-brain/SKILL.md', pathDisplay: '~/.claude/skills/radar-second-brain/SKILL.md', availableVersion: 1, installedVersion: state === 'not-installed' ? null : 1 });

async function open(p: ProjectPlan, skill: SkillStatus['state'] = 'up-to-date') {
  const api = {
    enableProject: vi.fn(async (_id: string, confirm: boolean, _access: boolean) => result(confirm, p)),
    skill: vi.fn(async () => skillStatus(skill)),
    installSkill: vi.fn(async () => skillStatus('up-to-date'))
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
    expect(api.enableProject).toHaveBeenCalledWith('api', false, true);
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

    expect(api.enableProject).toHaveBeenLastCalledWith('api', true, true);
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

  it('offers to let Claude read the vault without asking, on by default, and plans again when it is unticked', async () => {
    const access = { path: '.claude/settings.local.json', action: 'create' as const, excludeEntry: '/.claude/settings.local.json' };
    const { el, api, settle } = await open(plan({ access }));
    await settle();
    const box = el.querySelector<HTMLInputElement>('.acc input')!;
    expect(box.checked).toBe(true);
    expect(el.textContent).toContain('I will create .claude/settings.local.json with the vault folder.');
    expect(el.textContent).toContain('/.claude/settings.local.json in .git/info/exclude');
    box.click();
    await settle();
    expect(api.enableProject).toHaveBeenLastCalledWith('api', false, false);
  });

  it('says so when Claude can already read the vault, and when only a block can be reported', async () => {
    const done = await open(plan({ access: { path: '.claude/settings.local.json', action: 'unchanged' } }));
    await done.settle();
    expect(done.el.textContent).toContain('Claude can already read the vault without asking in this repository.');
    TestBed.resetTestingModule();

    const upd = await open(plan({ access: { path: '.claude/settings.local.json', action: 'update' } }));
    await upd.settle();
    expect(upd.el.textContent).toContain('I will add the vault folder to .claude/settings.local.json');
  });

  it('refuses to save while the access step is blocked and explains why', async () => {
    const { el, settle, button } = await open(plan({ access: { path: '.claude/settings.local.json', action: 'update', blocked: 'tracked' } }));
    await settle();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('already tracked by git');
    expect((button('SAVE') ?? button('ENABLE') ?? el.querySelector('.go')) as HTMLButtonElement).toHaveProperty('disabled', true);
  });

  it('shows the skill state and installs it when it is missing', async () => {
    const { el, api, settle, button } = await open(plan(), 'not-installed');
    await settle();
    expect(el.textContent).toContain('radar-second-brain: not installed');
    expect(el.textContent).toContain('Without the skill Claude does not know');
    button('INSTALL THE SKILL')!.click();
    await settle();
    expect(api.installSkill).toHaveBeenCalledWith(false);
    expect(el.textContent).toContain('installed ✓');
    expect(button('INSTALL THE SKILL')).toBeUndefined();
  });

  it('only reports a skill that is already installed', async () => {
    const { el, settle, button } = await open(plan(), 'up-to-date');
    await settle();
    expect(el.textContent).toContain('installed ✓');
    expect(button('INSTALL THE SKILL')).toBeUndefined();
  });

  it('mentions the access in the done screen when it was granted', async () => {
    const { el, settle, button } = await open(plan({ access: { path: '.claude/settings.local.json', action: 'create' } }));
    await settle();
    (el.querySelector('.go') as HTMLButtonElement).click();
    await settle();
    expect(el.textContent).toContain('Second brain is on');
    expect(el.textContent).toContain('Claude can already read the vault without asking in this repository.');
    expect(button('CLOSE')).toBeDefined();
  });
});
