import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { ScanResult } from '../core/models';
import { RadarApi } from '../core/radar-api';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { AgentComposer } from './agent-composer';

const AGENT = '---\nname: reviewer\ndescription: Reviews pull requests for correctness and style\ntools: Read\n---\n\nYou review code.\nRead the diff first.\n';

async function openDraft(hidden: boolean, kind: 'agent' | 'skill' | 'workflow' = 'agent') {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.result.set({ repos: [{ id: 'app', name: 'app', initials: 'AP', stack: '.NET', agents: [], skills: [], coverage: { score: 0 }, gaps: [], claudeMd: { exists: false, path: 'CLAUDE.md' } }], workflows: [], gaps: [] } as unknown as ScanResult);
  vi.spyOn(store, 'refreshQuiet').mockResolvedValue();
  const api = TestBed.inject(RadarApi);
  const create = vi.spyOn(api, 'createAgent').mockResolvedValue({ path: '.claude/agents/reviewer.md', hidden });
  const createItem = vi.spyOn(api, 'createItem').mockResolvedValue({ path: kind === 'skill' ? '.claude/skills/reviewer/SKILL.md' : '.claude/workflows/reviewer.md', hidden });
  store.mode.set('api');
  store.selectRepo('app');
  store.openComposer(kind);
  const fixture = TestBed.createComponent(AgentComposer);
  const c = fixture.componentInstance as unknown as { content: { set(v: string): void }; phase: { set(v: string): void } };
  c.content.set(AGENT);
  c.phase.set('draft');
  fixture.detectChanges();
  return { create, createItem, fixture, el: fixture.nativeElement as HTMLElement };
}

const save = (el: HTMLElement) => el.querySelector<HTMLButtonElement>('.btns.save .go')!;

describe('AgentComposer visibility', () => {
  it('saves a new agent as private unless the user picks public', async () => {
    const { create, fixture, el } = await openDraft(true);
    expect(el.querySelector('app-visibility-picker .opt.on')?.textContent).toContain('PRIVATE');
    save(el).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(create).toHaveBeenCalledWith('app', AGENT, 'private');
    expect(el.textContent).toContain('hid it from git');
  });

  it('sends public when chosen and then keeps the plain "not added to a commit" note', async () => {
    const { create, fixture, el } = await openDraft(false);
    el.querySelectorAll<HTMLButtonElement>('app-visibility-picker .opt')[1].click();
    fixture.detectChanges();
    save(el).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(create).toHaveBeenCalledWith('app', AGENT, 'public');
    expect(el.textContent).not.toContain('hid it from git');
  });

  it('drafts a skill: its own title, path and save call', async () => {
    const { create, createItem, fixture, el } = await openDraft(true, 'skill');
    expect(el.textContent).toContain('NEW SKILL · app');
    expect(el.textContent).toContain('.claude/skills/reviewer/SKILL.md');
    save(el).click();
    await fixture.whenStable();
    expect(createItem).toHaveBeenCalledWith('skill', 'app', AGENT, 'private');
    expect(create).not.toHaveBeenCalled();
  });

  it('drafts a workflow into .claude/workflows and offers agents and skills by name', async () => {
    const { createItem, fixture, el } = await openDraft(false, 'workflow');
    expect(el.textContent).toContain('NEW WORKFLOW · app');
    expect(el.textContent).toContain('.claude/workflows/reviewer.md');
    expect(el.textContent).toContain('so the workflow can refer to them');
    el.querySelectorAll<HTMLButtonElement>('app-visibility-picker .opt')[1].click();
    fixture.detectChanges();
    save(el).click();
    await fixture.whenStable();
    expect(createItem).toHaveBeenCalledWith('workflow', 'app', AGENT, 'public');
  });
});
