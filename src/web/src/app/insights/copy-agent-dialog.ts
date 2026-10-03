import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CopyTarget } from '../core/models';
import { ApiError } from '../core/radar-api';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';

type Phase = 'loading' | 'plan' | 'saving' | 'done' | 'error';

/** the most repos one copy goes to (the server's limit) */
const MAX_TARGETS = 100;

/**
 * Copies one agent into other repos, opened from the agent's file pane. The server first reports where a copy can go (nothing is
 * written), the user ticks the repos, and only the confirm button writes: new files, never overwriting. Nothing is ticked by default.
 */
@Component({
  selector: 'app-copy-agent-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './copy-agent-dialog.html',
  styleUrl: './copy-agent-dialog.scss'
})
export class CopyAgentDialog {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;

  protected readonly ref = computed(() => this.store.copyAgentRef());
  protected readonly source = computed(() => this.store.repos().find((r) => r.id === this.ref()?.repoId) ?? null);
  protected readonly phase = signal<Phase>('loading');
  protected readonly error = signal<string | null>(null);
  protected readonly targets = signal<CopyTarget[]>([]);
  protected readonly chosen = signal<ReadonlySet<string>>(new Set());
  protected readonly results = signal<CopyTarget[]>([]);

  /** repos other than the source that have no agent of this name, the ones with the source's stack first */
  private readonly candidates = computed(() => {
    const src = this.source();
    const name = this.ref()?.name.toLowerCase();
    if (!src || !name) return [];
    return this.store.repos()
      .filter((r) => r.id !== src.id && !r.agents.some((a) => a.name.toLowerCase() === name))
      .sort((a, b) => Number(b.stack === src.stack) - Number(a.stack === src.stack) || a.name.localeCompare(b.name))
      .slice(0, MAX_TARGETS);
  });
  protected readonly stackOf = computed(() => new Map(this.store.repos().map((r) => [r.id, r.stack])));
  protected readonly readyTargets = computed(() => this.targets().filter((x) => x.status === 'ready'));
  protected readonly skipped = computed(() => this.targets().filter((x) => x.status !== 'ready'));

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    const ref = this.ref();
    const ids = this.candidates().map((r) => r.id);
    if (!ref) return;
    if (ids.length === 0) { this.phase.set('plan'); return; }
    try {
      this.targets.set((await this.store.planCopyAgent(ref.repoId, ref.name, ids)).targets);
      this.phase.set('plan');
    } catch (e) {
      this.error.set(e instanceof ApiError ? e.message : this.t('copy.planFailed'));
      this.phase.set('error');
    }
  }

  protected toggle(id: string): void {
    const next = new Set(this.chosen());
    if (next.has(id)) next.delete(id); else next.add(id);
    this.chosen.set(next);
  }

  protected async confirm(): Promise<void> {
    const ref = this.ref();
    const ids = this.readyTargets().map((x) => x.repoId).filter((id) => this.chosen().has(id));
    if (!ref || ids.length === 0 || this.phase() !== 'plan') return;
    this.error.set(null);
    this.phase.set('saving');
    try {
      this.results.set((await this.store.applyCopyAgent(ref.repoId, ref.name, ids)).targets);
      this.phase.set('done');
    } catch (e) {
      this.error.set(e instanceof ApiError ? e.message : this.t('copy.saveFailed'));
      this.phase.set('plan');
    }
  }

  protected statusText(r: CopyTarget): string {
    return r.status === 'created' ? this.t('copy.status.created') : this.t('copy.status.skipped');
  }

  protected created(): number {
    return this.results().filter((x) => x.status === 'created').length;
  }
}
