import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { SkillStatus, VaultStatus } from '../core/models';
import { ApiError, RadarApi } from '../core/radar-api';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { VaultDialog } from './vault-dialog';

const status = (state: VaultStatus['state'], extra: Partial<VaultStatus> = {}): VaultStatus => ({
  state, path: '/data/brain', pathDisplay: '~/brain', projects: [], suggestedPath: '/home/me/Documents/RADAR Second Brain',
  suggestedPathDisplay: '~/Documents/RADAR Second Brain', ...extra
});
const skill = (state: SkillStatus['state']): SkillStatus => ({
  name: 'radar-second-brain', state, path: '/c/skills/radar-second-brain/SKILL.md', pathDisplay: '~/.claude/skills/radar-second-brain/SKILL.md', availableVersion: 1
});

async function open(initial: VaultStatus, skillState: SkillStatus['state'] = 'not-installed') {
  const api = {
    skill: vi.fn(async () => skill(skillState)),
    installSkill: vi.fn(async (_update: boolean) => skill('up-to-date')),
    createVault: vi.fn(async (_p: string) => status('ok')),
    relinkVault: vi.fn(async (_p: string) => status('ok')),
    pickFolder: vi.fn(async () => '/picked/folder')
  };
  TestBed.configureTestingModule({ providers: [provideHttpClient(), { provide: RadarApi, useValue: api }] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.vault.set(initial);
  store.settings.set({ scanPath: '/r', scanPathDisplay: '/r', exists: true, maxDepth: 4, canPickFolder: true });
  const fixture = TestBed.createComponent(VaultDialog);
  await fixture.whenStable();
  const el = fixture.nativeElement as HTMLElement;
  // the mocked API resolves on later ticks, which Angular does not track: wait a macrotask, then render
  const settle = async () => { await new Promise((r) => setTimeout(r)); fixture.detectChanges(); await fixture.whenStable(); };
  const click = async (label: string) => {
    const b = Array.from(el.querySelectorAll('button')).find((x) => x.textContent?.trim() === label);
    if (!b) throw new Error(`no button "${label}" in: ${el.textContent}`);
    b.click();
    await settle();
  };
  const type = async (value: string) => {
    const input = el.querySelector('input') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle();
  };
  return { api, store, el, click, type, settle };
}

describe('VaultDialog', () => {
  it('walks a new user from the intro to the location step with the suggested folder', async () => {
    const { el, click } = await open(status('none'));
    expect(el.textContent).toContain('Knowledge about your projects');
    await click('NEXT');
    expect((el.querySelector('input') as HTMLInputElement).value).toBe('/home/me/Documents/RADAR Second Brain');
    expect(el.textContent).toContain('cannot be inside a git repository');
  });

  it('offers the native folder dialog only when the server has one', async () => {
    const { el, click, api, settle } = await open(status('none'));
    await click('NEXT');
    await click('CHOOSE…');
    expect(api.pickFolder).toHaveBeenCalled();
    expect((el.querySelector('input') as HTMLInputElement).value).toBe('/picked/folder');

    TestBed.inject(RadarStore).settings.update((s) => (s ? { ...s, canPickFolder: false } : s));
    await settle();
    expect(Array.from(el.querySelectorAll('button')).some((b) => b.textContent?.trim() === 'CHOOSE…')).toBe(false);
  });

  it('creates the vault and shows it with its skill section', async () => {
    const { el, click, type, api, store } = await open(status('none'));
    await click('NEXT');
    await type('/data/brain');
    await click('CREATE');
    expect(api.createVault).toHaveBeenCalledWith('/data/brain');
    expect(store.vaultReady()).toBe(true);
    expect(el.textContent).toContain('The second brain is ready.');
    expect(el.textContent).toContain('SKILL FOR WORKING WITH IT');
  });

  it('offers a subfolder when the chosen folder is not empty and creates there on request', async () => {
    const { el, click, type, api } = await open(status('none'));
    api.createVault.mockRejectedValueOnce(new ApiError('The chosen folder is not empty.', 409, { code: 'not-empty' }));
    await click('NEXT');
    await type('/data/notes/');
    await click('CREATE');
    expect(el.textContent).toContain('The chosen folder is not empty.');
    expect(el.textContent).toContain('/data/notes/RADAR Second Brain');

    await click('CREATE IN A SUBFOLDER');
    expect(api.createVault).toHaveBeenLastCalledWith('/data/notes/RADAR Second Brain');
    expect(el.textContent).toContain('The second brain is ready.');
  });

  it('offers to use an existing vault found in the chosen folder', async () => {
    const { el, click, type, api } = await open(status('none'));
    api.createVault.mockRejectedValueOnce(new ApiError('Already there.', 409, { code: 'already-vault' }));
    await click('NEXT');
    await type('/data/brain');
    await click('CREATE');
    await click('USE THE EXISTING ONE');
    expect(api.relinkVault).toHaveBeenCalledWith('/data/brain');
    expect(el.textContent).toContain('~/brain');
  });

  it('asks for the new place when the folder was moved, and says so', async () => {
    const { el, click, type, api, store } = await open(status('missing', { reason: 'folder-missing' }));
    expect(el.textContent).toContain('is gone');
    expect(el.textContent).toContain('does not move files');
    expect((el.querySelector('input') as HTMLInputElement).value).toBe('/data/brain');

    await type('/data/moved');
    await click('POINT TO IT');
    expect(api.relinkVault).toHaveBeenCalledWith('/data/moved');
    expect(store.vaultReady()).toBe(true);
  });

  it('shows the server message when pointing to a folder fails', async () => {
    const { el, click, type, api } = await open(status('missing', { reason: 'not-a-vault' }));
    api.relinkVault.mockRejectedValueOnce(new ApiError('This is not a second brain folder.', 400, { code: 'not-a-vault' }));
    await type('/elsewhere');
    await click('POINT TO IT');
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('This is not a second brain folder.');
  });

  it('installs the skill only when asked and then shows it as installed', async () => {
    const { el, click, api } = await open(status('ok', { projects: [{ name: 'api', repoId: 'api' }] }));
    expect(el.textContent).toContain('not installed');
    expect(el.textContent).toContain('api');
    expect(api.installSkill).not.toHaveBeenCalled();

    await click('INSTALL THE SKILL');
    expect(api.installSkill).toHaveBeenCalledWith(false);
    expect(el.textContent).toContain('installed ✓');
    expect(Array.from(el.querySelectorAll('button')).some((b) => b.textContent?.includes('INSTALL THE SKILL'))).toBe(false);
  });

  it('asks for an update only for an outdated skill and never offers one for a skill it must leave alone', async () => {
    const outdated = await open(status('ok'), 'outdated');
    await outdated.click('UPDATE THE SKILL');
    expect(outdated.api.installSkill).toHaveBeenCalledWith(true);
    TestBed.resetTestingModule();

    for (const state of ['modified', 'foreign', 'linked'] as const) {
      const d = await open(status('ok'), state);
      expect(Array.from(d.el.querySelectorAll('button')).some((b) => /SKILL/.test(b.textContent ?? '') && /INSTALL|UPDATE/.test(b.textContent ?? ''))).toBe(false);
      expect(d.el.textContent).toContain('leaves it alone');
      TestBed.resetTestingModule();
    }
  });

  it('shows a refusal from the skill installer', async () => {
    const { el, click, api } = await open(status('ok'));
    api.installSkill.mockRejectedValueOnce(new ApiError('The skill was edited by hand.', 409));
    await click('INSTALL THE SKILL');
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('edited by hand');
  });
});
