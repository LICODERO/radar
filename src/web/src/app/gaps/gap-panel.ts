import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { GapType } from '../core/models';
import { ApiError } from '../core/radar-api';
import { GAP_ORDER, GapItem, Shell, Tool, buildCommand, filterItems, toScript } from '../core/commands';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { MsgKey } from '../i18n/pl';

const MAX_SHOWN = 100;

@Component({
  selector: 'app-gap-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './gap-panel.html',
  styleUrl: './gap-panel.scss'
})
export class GapPanel {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  protected readonly tool = signal<Tool>('claude');
  protected readonly label = (g: GapType): string => this.t(('gap.' + g) as MsgKey);
  protected readonly order = GAP_ORDER;
  protected readonly copied = signal<string | null>(null);
  protected readonly pending = this.store.pendingRun;
  protected readonly running = signal(false);
  protected readonly runError = signal<string | null>(null);
  protected readonly launched = signal<string | null>(null);

  protected readonly shell = computed<Shell>(() => this.store.tools()?.shell ?? 'posix');
  protected readonly canLaunch = computed(() => this.store.tools()?.canLaunch === true);
  protected readonly terminalName = computed(() => {
    const app = this.store.tools()?.terminal;
    if (app) return app;
    switch (this.store.tools()?.platform) {
      case 'macos': return 'Terminal.app';
      case 'windows': return 'PowerShell';
      default: return this.t('gaps.terminal.generic');
    }
  });

  /** default filter: the gaps counted in the KPI; when there are none, every type that has gaps */
  protected readonly selected = signal<ReadonlySet<GapType>>(this.initialSelection());
  protected readonly items = computed(() => filterItems(this.store.gapItems(), this.selected()));
  protected readonly shown = computed(() => this.items().slice(0, MAX_SHOWN).map((i) => ({
    ...i, key: i.repoId + '|' + i.type, cmd: buildCommand(this.shell(), this.tool(), i.dir, i.prompt)
  })));
  protected readonly toolName = computed(() => (this.tool() === 'claude' ? 'Claude Code' : 'Codex CLI'));

  private initialSelection(): ReadonlySet<GapType> {
    const t = this.store.gapTotals();
    if (t['no-claude-md'] > 0) return new Set<GapType>(['no-claude-md']);
    return new Set(GAP_ORDER.filter((g) => t[g] > 0));
  }

  protected toggle(t: GapType): void {
    const next = new Set(this.selected());
    if (next.has(t)) next.delete(t); else next.add(t);
    this.selected.set(next);
  }

  protected async copy(key: string, text: string): Promise<void> {
    if (await copyToClipboard(text)) {
      this.copied.set(key);
      setTimeout(() => { if (this.copied() === key) this.copied.set(null); }, 1600);
    }
  }

  protected copyAll(): Promise<void> {
    return this.copy('all', toScript(this.shell(), this.tool(), this.items(), (i) => this.t('gaps.scriptLine', { repo: i.repoName, gap: this.label(i.type) })));
  }

  protected askRun(item: GapItem): void {
    this.runError.set(null);
    this.pending.set({
      item,
      command: buildCommand(this.shell(), this.tool(), item.dir, item.prompt, this.store.tools()?.toolPaths?.[this.tool()]),
      toolFound: this.store.tools()?.tools[this.tool()] !== false
    });
  }

  protected cancelRun(): void {
    if (!this.running()) this.pending.set(null);
  }

  protected async confirmRun(): Promise<void> {
    const p = this.pending();
    if (!p || this.running()) return;
    this.running.set(true);
    this.runError.set(null);
    try {
      await this.store.runGap(p.item, this.tool());
      const key = p.item.repoId + '|' + p.item.type;
      this.launched.set(key);
      setTimeout(() => { if (this.launched() === key) this.launched.set(null); }, 4000);
      this.pending.set(null);
    } catch (e) {
      this.runError.set(e instanceof ApiError ? e.message : this.t('gaps.runFailed'));
    } finally {
      this.running.set(false);
    }
  }
}

async function copyToClipboard(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    // clipboard API unavailable (insecure context / permissions): fall back to a temporary textarea
    const ta = document.createElement('textarea');
    ta.value = text;
    ta.style.position = 'fixed';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    try { return document.execCommand('copy'); } finally { ta.remove(); }
  }
}
