import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { ScanResult } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { ItemsDialog } from './items-dialog';

const agents = Array.from({ length: 9 }, (_, i) => ({ name: 'agent-' + i, description: i % 2 ? '' : 'does thing ' + i, tools: [], path: `.claude/agents/agent-${i}.md` }));
const skills = [{ name: 'ef-migrations', description: 'EF steps', path: '.claude/skills/ef/SKILL.md' }];
const repo = { id: 'api', name: 'api', initials: 'AP', stack: '.NET', agents, skills, coverage: { score: 50 }, gaps: [], claudeMd: { exists: true, path: 'CLAUDE.md' } };
const workflows = [{ id: 'W1', name: 'pr-review', description: 'Reviews PRs', when: 'on PR', agents: [], repos: [{ repoId: 'api', path: 'x', linked: true }], issues: [] }];

async function open(kind: 'a' | 's' | 'w') {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.result.set({ repos: [repo], workflows, gaps: [] } as unknown as ScanResult);
  store.selectRepo('api');
  store.openItems(kind);
  const fixture = TestBed.createComponent(ItemsDialog);
  await fixture.whenStable();
  fixture.detectChanges();
  return { store, el: fixture.nativeElement as HTMLElement, fixture };
}

describe('ItemsDialog', () => {
  it('lists every agent of the selected repo, with its description or a placeholder', async () => {
    const { el } = await open('a');
    expect(el.querySelectorAll('.row').length).toBe(9);
    expect(el.textContent).toContain('AGENTS · 9');
    expect(el.textContent).toContain('does thing 0');
    expect(el.textContent).toContain('no description');
    expect(el.querySelector('.ttl')?.textContent).toBe('api');
  });

  it('lists skills and the repo\'s workflows too', async () => {
    const skills = await open('s');
    expect(skills.el.querySelectorAll('.row').length).toBe(1);
    expect(skills.el.textContent).toContain('SKILLS · 1');
    TestBed.resetTestingModule();

    const wf = await open('w');
    expect(wf.el.textContent).toContain('pr-review');
    expect(wf.el.textContent).toContain('Reviews PRs');
  });

  it('picking an agent closes the list and opens its details on the orbit', async () => {
    const { store, el, fixture } = await open('a');
    (el.querySelectorAll('.row')[2] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(store.itemsKind()).toBeNull();
    expect(store.pick()).toEqual({ kind: 'a', repoId: 'api', name: 'agent-2' });
    expect(store.selected()?.id).toBe('api');
  });

  it('picking a workflow keeps the selected repo, like the chips on its card', async () => {
    const { store, el } = await open('w');
    (el.querySelector('.row') as HTMLButtonElement).click();
    expect(store.pick()).toEqual({ kind: 'w', name: 'W1' });
    expect(store.selected()?.id).toBe('api');
  });

  it('only opens for a selected repo', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient()] });
    const store = TestBed.inject(RadarStore);
    store.openItems('a');
    expect(store.itemsKind()).toBeNull();
  });

  it('tags each agent with its git visibility, and nothing for scans that predate it', async () => {
    const { el, store, fixture } = await open('a');
    expect(el.querySelector('.tag')).toBeNull();
    store.result.set({ repos: [{ ...repo, agents: [{ ...agents[0], visibility: 'private' }, { ...agents[1], visibility: 'public' }] }], workflows, gaps: [] } as unknown as ScanResult);
    fixture.detectChanges();
    const tags = Array.from(el.querySelectorAll('.tag')).map((t) => t.textContent?.trim());
    expect(tags).toEqual(['PRIVATE', 'PUBLIC']);
  });
});
