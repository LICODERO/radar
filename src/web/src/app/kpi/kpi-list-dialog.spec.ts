import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { ScanResult } from '../core/models';
import { KpiKind, RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { KpiListDialog } from './kpi-list-dialog';

const agent = (name: string) => ({ name, description: name + ' does things', tools: [], path: `.claude/agents/${name}.md` });
const skill = (name: string) => ({ name, description: name + ' skill', path: `.claude/skills/${name}/SKILL.md` });
const repo = (id: string, agents: string[], skills: string[], score: number) => ({
  id, name: id, initials: id.slice(0, 2).toUpperCase(), stack: '.NET', agents: agents.map(agent), skills: skills.map(skill), coverage: { score }, claudeMd: { exists: true, path: 'CLAUDE.md' }, gaps: []
});

async function open(kind: KpiKind) {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.result.set({ repos: [repo('shop', ['tester', 'builder'], ['pdf'], 90), repo('crm', ['tester'], [], 20)], workflows: [], gaps: [] } as unknown as ScanResult);
  store.openKpiList(kind);
  const fixture = TestBed.createComponent(KpiListDialog);
  await fixture.whenStable();
  fixture.detectChanges();
  const rows = () => [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.row')];
  return { store, el: fixture.nativeElement as HTMLElement, rows };
}

describe('KpiListDialog', () => {
  it('lists every repo with its coverage and selects the one that is clicked', async () => {
    const { store, el, rows } = await open('repos');
    expect(el.querySelector('.eyebrow')?.textContent).toBe('REPOSITORIES · 2');
    const part = (r: HTMLElement, c: string) => r.querySelector(c)?.textContent;
    expect(rows().map((r) => [part(r, '.name'), part(r, '.sub'), part(r, '.side')])).toEqual([['SH shop', '.NET · A2 · S1', '90%'], ['CR crm', '.NET · A1 · S0', '20%']]);
    rows()[1].click();
    expect(store.selected()?.id).toBe('crm');
    expect(store.kpiList()).toBeNull();
  });

  it('lists the agents of all repos by name and opens the one that is clicked', async () => {
    const { store, el, rows } = await open('agents');
    expect(el.querySelector('.eyebrow')?.textContent).toBe('AGENTS · 3');
    expect(rows().map((r) => r.querySelector('.name')?.textContent + '@' + r.querySelector('.side')?.textContent)).toEqual(['builder@shop', 'tester@crm', 'tester@shop']);
    rows()[1].click();
    expect(store.selected()?.id).toBe('crm');
    expect(store.pick()).toMatchObject({ kind: 'a', repoId: 'crm', name: 'tester' });
  });

  it('lists the skills of all repos', async () => {
    const { el, rows } = await open('skills');
    expect(el.querySelector('.eyebrow')?.textContent).toBe('SKILLS · 1');
    expect(rows().length).toBe(1);
  });
});

describe('opening a list', () => {
  it('does nothing when there is nothing to list', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient()] });
    const store = TestBed.inject(RadarStore);
    store.result.set({ repos: [repo('crm', [], [], 0)], workflows: [], gaps: [] } as unknown as ScanResult);
    store.openKpiList('agents');
    expect(store.kpiList()).toBeNull();
    store.openKpiList('repos');
    expect(store.kpiList()).toBe('repos');
  });
});
