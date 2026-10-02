import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { ToolStatus } from '../core/models';
import { RadarApi } from '../core/radar-api';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { AiTools } from './ai-tools';

const status = (tool: string, found: boolean, version: string | null = null): ToolStatus => ({ tool, found, path: found ? '/bin/' + tool : null, version });

async function render(cli: ToolStatus[] | null | undefined) {
  const api = { gaps: async () => [], startScan: vi.fn(async () => 'scan-1'), openEvents: async () => ({ addEventListener: () => {}, close: () => {}, readyState: 0 }) };
  TestBed.configureTestingModule({ providers: [provideHttpClient(), { provide: RadarApi, useValue: api }] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.cli.set(cli);
  const fixture = TestBed.createComponent(AiTools);
  await fixture.whenStable();
  fixture.detectChanges();
  return { el: fixture.nativeElement as HTMLElement, store, fixture, api };
}

const all = [status('claude', true, '2.1.5'), status('codex', false), status('cursor', true), status('antigravity', false)];

describe('AiTools', () => {
  it('lists the four tools coloured by whether they are installed', async () => {
    const { el } = await render(all);
    const rows = [...el.querySelectorAll('.ai-row')];
    expect(rows.map((r) => r.querySelector('.ai-name')?.textContent)).toEqual(['Claude Code', 'Codex CLI', 'Cursor', 'Antigravity']);
    expect(rows.map((r) => (r.classList.contains('ok') ? 'ok' : r.classList.contains('missing') ? 'missing' : '?'))).toEqual(['ok', 'missing', 'ok', 'missing']);
    expect(rows[0].textContent).toContain('v2.1.5');
    expect(rows[2].textContent).toContain('version unknown');
  });

  it('only available tools can be picked; Claude is the one selected by default', async () => {
    const { el } = await render(all);
    const rows = [...el.querySelectorAll<HTMLButtonElement>('.ai-row')];
    expect(rows.map((r) => r.disabled)).toEqual([false, true, false, true]);
    expect(rows.map((r) => r.getAttribute('aria-checked'))).toEqual(['true', 'false', 'false', 'false']);
  });

  it('picking an available tool shows a loader, then switches the view without starting a scan', async () => {
    const { el, store, fixture, api } = await render(all);
    store.settings.set({ scanPath: '/repos', exists: true } as never);
    const picked = store.selectAiTool('cursor');
    expect(store.switching()).toBe(true);
    await picked;
    fixture.detectChanges();
    expect(store.switching()).toBe(false);
    expect(store.aiTool()).toBe('cursor');
    expect(store.aiToolScanned()).toBe(false);
    expect(api.startScan).not.toHaveBeenCalled();
    expect(store.scan()).toBeNull();
    expect(el.querySelectorAll('.ai-row')[2].classList.contains('sel')).toBe(true);
  });

  it('switching back to Claude restores its saved scan without scanning again', async () => {
    const { store, api } = await render(all);
    store.settings.set({ scanPath: '/repos', exists: true } as never);
    const saved = { repos: [], workflows: [], gaps: [], summary: { avgCoverage: 10 } } as never;
    store.result.set(saved);
    await store.selectAiTool('cursor');
    expect(store.result()).toBeNull();
    await store.selectAiTool('claude');
    expect(store.result()).toBe(saved);
    expect(store.aiTool()).toBe('claude');
    expect(api.startScan).not.toHaveBeenCalled();
    expect(store.canScan()).toBe(true);
  });

  it('the scan button is off for a tool that cannot be scanned yet', async () => {
    const { store } = await render(all);
    store.settings.set({ scanPath: '/repos', exists: true } as never);
    expect(store.canScan()).toBe(true);
    await store.selectAiTool('cursor');
    expect(store.canScan()).toBe(false);
  });

  it('refuses a tool that is not installed', async () => {
    const { store } = await render(all);
    await store.selectAiTool('codex');
    expect(store.aiTool()).toBe('claude');
  });

  it('shows an unknown state before the first check', async () => {
    const { el } = await render(undefined);
    expect(el.querySelectorAll('.ai-row.unknown').length).toBe(4);
  });
});
