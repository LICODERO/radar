import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CopyTarget } from '../core/models';
import { ApiError } from '../core/radar-api';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';

type Phase = 'loading' | 'plan' | 'saving' | 'done' | 'error';

/**
 * Spreads an agent that some repos have to the repos that lack it. The server first reports where a copy can go (nothing is written),
 * the user picks the repos and the version, and only the confirm button writes: new files, never overwriting.
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

  protected readonly item = computed(() => this.store.copyAgentItem());
  protected readonly phase = signal<Phase>('loading');
  protected readonly error = signal<string | null>(null);
  /** version (index into the item's variants) the copies are made from; the most widespread one is first */
  protected readonly version = signal(0);
  protected readonly targets = signal<CopyTarget[]>([]);
  protected readonly chosen = signal<ReadonlySet<string>>(new Set());
  protected readonly results = signal<CopyTarget[]>([]);

  protected readonly source = computed(() => this.item()?.variants[this.version()]?.repos[0] ?? '');
  protected readonly readyTargets = computed(() => this.targets().filter((x) => x.status === 'ready'));
  protected readonly skipped = computed(() => this.targets().filter((x) => x.status !== 'ready'));
  protected readonly letters = 'ABCDEFGHIJ';

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    const item = this.item();
    if (!item) return;
    try {
      const plan = await this.store.planCopyAgent(this.source(), item.name, item.missing);
      this.targets.set(plan.targets);
      this.chosen.set(new Set(plan.targets.filter((x) => x.status === 'ready').map((x) => x.repoId)));
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

  protected pickVersion(i: number): void {
    this.version.set(i);
  }

  protected async confirm(): Promise<void> {
    const item = this.item();
    const ids = this.readyTargets().map((x) => x.repoId).filter((id) => this.chosen().has(id));
    if (!item || ids.length === 0 || this.phase() !== 'plan') return;
    this.error.set(null);
    this.phase.set('saving');
    try {
      this.results.set((await this.store.applyCopyAgent(this.source(), item.name, ids)).targets);
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
