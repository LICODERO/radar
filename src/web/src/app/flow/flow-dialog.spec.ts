import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { FlowPlan, FlowRule, ScanResult } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { FlowDialog } from './flow-dialog';

const rule = (over: Partial<FlowRule> = {}): FlowRule => ({ from: 'web', to: 'api', kind: 'rest', auth: 'oauth-client', authEnv: ['API_ID', 'API_SECRET'], contract: null, notes: null, visibility: 'public', direction: 'out', ...over });
const plan = (over: Partial<FlowPlan> = {}): FlowPlan => ({
  repos: [{ repo: 'web', parts: [{ id: 'relations', file: '.claude/relations.md', action: 'create', text: '# Flow between repos' }], notes: [] }],
  blocked: null, applied: false, ...over
});
const repo = (name: string) => ({ id: name, name, initials: name.slice(0, 2).toUpperCase(), stack: '.NET', agents: [{ name: 'dev' }], skills: [], coverage: { score: 50 }, gaps: [], claudeMd: { exists: true, path: 'CLAUDE.md' } });

async function open(rules: FlowRule[], planned = plan(), applied?: FlowPlan) {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.mode.set('api');
  store.result.set({ repos: [repo('web'), repo('api'), repo('db')], workflows: [], gaps: [] } as unknown as ScanResult);
  vi.spyOn(store, 'flowRules').mockResolvedValue(rules);
  const planSpy = vi.spyOn(store, 'planFlow').mockResolvedValue(planned);
  const applySpy = vi.spyOn(store, 'applyFlow').mockResolvedValue(applied ?? { ...planned, applied: true });
  store.flowOpen.set(true);
  const fixture = TestBed.createComponent(FlowDialog);
  await fixture.whenStable();
  fixture.detectChanges();
  return { store, planSpy, applySpy, fixture, el: fixture.nativeElement as HTMLElement };
}

const go = (el: HTMLElement) => el.querySelector<HTMLButtonElement>('.go')!;

describe('FlowDialog', () => {
  beforeEach(() => { try { localStorage.removeItem('radar.flow.layout'); } catch { /* none */ } });

  it('puts the repos of the saved rules on the board with a labelled connection', async () => {
    const { el } = await open([rule()]);
    expect([...el.querySelectorAll('.card')].map((c) => (c as HTMLElement).dataset['card'])).toEqual(['web', 'api']);
    expect(el.querySelectorAll('.tag').length).toBe(1);
    expect(el.querySelector('.tag')!.textContent).toContain('REST API');
    expect(go(el).disabled).toBe(true);                       // nothing changed yet
    expect(el.textContent).toContain('Everything saved');
  });

  it('adds a repo to the board from the list, once', async () => {
    const { el, fixture } = await open([]);
    const items = () => [...el.querySelectorAll<HTMLButtonElement>('app-flow-list.repos button')];
    items().find((b) => b.textContent!.includes('db'))!.click();
    fixture.detectChanges();
    expect(el.querySelectorAll('.card').length).toBe(1);
    expect(items().find((b) => b.textContent!.includes('db'))!.disabled).toBe(true);
    expect(el.textContent).toContain('1 agent');             // the plural comes from the shared key
  });

  it('puts a repo dropped from the list where it landed on the board', async () => {
    const { el, fixture } = await open([]);
    const c = fixture.componentInstance as unknown as { onDrop(e: DragEvent): void; pos(): Record<string, { x: number; y: number }> };
    vi.spyOn(c as any, 'world').mockReturnValue({ x: 400, y: 300 });
    const e = { preventDefault: () => undefined, dataTransfer: { getData: () => 'db' } } as unknown as DragEvent;
    c.onDrop(e);
    fixture.detectChanges();
    expect(el.querySelectorAll('.card').length).toBe(1);
    expect(c.pos()['db']).toEqual({ x: 400 - 105, y: 300 - 35 });
  });

  it('plans first and only writes after the confirm button', async () => {
    const { el, fixture, planSpy, applySpy, store } = await open([rule()]);
    fixture.componentInstance['select'](fixture.componentInstance['edges']()[0].key);
    fixture.componentInstance['setVisibility']('private');
    fixture.detectChanges();
    expect(go(el).disabled).toBe(false);
    go(el).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(planSpy).toHaveBeenCalledWith([expect.objectContaining({ visibility: 'private' })]);
    expect(applySpy).not.toHaveBeenCalled();
    expect(el.textContent).toContain('web');
    expect(el.textContent).not.toContain('.claude/relations.md');            // the repo block starts closed
    el.querySelector<HTMLButtonElement>('.rh')!.click();
    fixture.detectChanges();
    expect(el.textContent).toContain('.claude/relations.md');
    el.querySelector<HTMLButtonElement>('.ph')!.click();
    fixture.detectChanges();
    expect(el.querySelector('.block')!.textContent).toContain('# Flow between repos');
    el.querySelector<HTMLButtonElement>('.go')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(applySpy).toHaveBeenCalledTimes(1);
    expect(el.textContent).toContain('Rules saved');
    expect(store.flowDirty()).toBe(false);
  });

  it('filters the repo list by what is typed and says so when nothing matches', async () => {
    const { el, fixture } = await open([]);
    const q = el.querySelector<HTMLInputElement>('app-flow-list.repos input')!;
    const names = () => [...el.querySelectorAll('app-flow-list.repos li .rn')].map((n) => n.textContent);
    expect(names()).toEqual(['api', 'db', 'web']);
    q.value = 'D';
    q.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(names()).toEqual(['db']);
    q.value = 'zzz';
    q.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(el.querySelector('app-flow-list.repos .none')).not.toBeNull();
  });

  it('offers a data direction only where the kind does not already say it, and resets it for request/response kinds', async () => {
    const { el, fixture } = await open([rule({ kind: 'events', auth: 'none', authEnv: [] })]);
    const c = fixture.componentInstance as unknown as { select(k: string): void; edges(): { key: string }[]; setDirection(d: string): void; setKind(k: string): void; selected(): FlowRule };
    c.select(c.edges()[0].key);
    fixture.detectChanges();
    expect(el.querySelectorAll('app-flow-editor .seg').length).toBe(2);            // direction + visibility
    c.setDirection('both');
    fixture.detectChanges();
    expect(c.selected().direction).toBe('both');
    expect(el.querySelector('.wire')!.getAttribute('marker-start')).toContain('ah-events');
    expect(el.querySelector('.wire')!.getAttribute('marker-end')).toContain('ah-events');
    c.setKind('rest');
    fixture.detectChanges();
    expect(c.selected().direction).toBe('out');
    expect(el.querySelectorAll('app-flow-editor .seg').length).toBe(1);
    expect(el.querySelector('.wire')!.getAttribute('marker-start')).toBeNull();
  });

  it('turns a connection around and keeps its data direction meaningful', async () => {
    const { fixture } = await open([rule({ kind: 'events', auth: 'none', authEnv: [], direction: 'out' })]);
    const c = fixture.componentInstance as unknown as { select(k: string): void; edges(): { key: string }[]; swap(): void; selected(): FlowRule };
    c.select(c.edges()[0].key);
    c.swap();
    expect(c.selected()).toMatchObject({ from: 'api', to: 'web', direction: 'in' });   // web still sends to api, now read from api's side
  });

  it('gives every card a dot on each of its four sides', async () => {
    const { el } = await open([rule()]);
    const first = el.querySelector('.card')!;
    expect([...first.querySelectorAll('.port')].map((p) => (p as HTMLElement).dataset['port']).sort()).toEqual(['bottom', 'left', 'right', 'top']);
  });

  it('refuses a secret value where a variable name belongs', async () => {
    const { el, fixture } = await open([rule()]);
    const c = fixture.componentInstance;
    c['select'](c['edges']()[0].key);
    c['setEnv']('sk_live=abc123');
    fixture.detectChanges();
    expect(el.textContent).toContain('does not look like a variable name');
    expect(c['selected']()!.authEnv).toEqual(['API_ID', 'API_SECRET']);   // the bad text was not accepted
    expect(go(el).disabled).toBe(true);
  });

  it('keeps unsaved rules when Escape or the backdrop would close it, but not for the close button', async () => {
    const { store, fixture } = await open([rule()]);
    fixture.componentInstance['setVisibility']('private');   // no rule selected: nothing changes
    fixture.componentInstance['select'](fixture.componentInstance['edges']()[0].key);
    fixture.componentInstance['setVisibility']('private');
    fixture.detectChanges();
    expect(store.flowDirty()).toBe(true);
    store.closeFlow();
    expect(store.flowOpen()).toBe(true);
    store.closeFlow(true);
    expect(store.flowOpen()).toBe(false);
  });

  it('shows why nothing is written when the server blocks the plan', async () => {
    const { el, fixture } = await open([rule()], plan({ repos: [], blocked: 'local-tracked', blockedRepo: 'web' }));
    fixture.componentInstance['select'](fixture.componentInstance['edges']()[0].key);
    fixture.componentInstance['setVisibility']('private');
    fixture.detectChanges();
    go(el).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.textContent).toContain('already tracked by git');
    expect(el.textContent).toContain('web');
    expect(go(el).disabled).toBe(true);
  });
});
