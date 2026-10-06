import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CopyAgentResult, CopyTarget } from '../core/models';
import { ApiError } from '../core/radar-api';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { MsgKey } from '../i18n/pl';
import { NewVisibility, VisibilityPicker } from '../shared/visibility-picker';

type Phase = 'loading' | 'plan' | 'saving' | 'done' | 'error';

/** the most repos one copy goes to (the server's limit) */
const MAX_TARGETS = 100;

/**
 * Copies one agent into other repos, opened from the agent's file pane. The server first reports where a copy can go (nothing is
 * written), the user ticks the repos, and only the confirm button writes: new files, never overwriting. Nothing is ticked by default.
 */
@Component({
  selector: 'app-copy-agent-dialog',
  imports: [VisibilityPicker],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './copy-agent-dialog.html',
  styleUrl: './copy-agent-dialog.scss'
})
export class CopyAgentDialog {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;

  protected readonly ref = computed(() => this.store.copyAgentRef());
  protected readonly kind = computed(() => this.ref()?.kind ?? 'agent');
  protected readonly source = computed(() => this.store.repos().find((r) => r.id === this.ref()?.repoId) ?? null);
  protected readonly phase = signal<Phase>('loading');
  protected readonly error = signal<string | null>(null);
  protected readonly targets = signal<CopyTarget[]>([]);
  protected readonly chosen = signal<ReadonlySet<string>>(new Set());
  protected readonly results = signal<CopyTarget[]>([]);
  protected readonly plan = signal<CopyAgentResult | null>(null);
  /** what travels with a skill: files and what was left out */
  protected readonly skillFiles = computed(() => this.plan()?.files ?? []);
  protected readonly leftOut = computed(() => this.plan()?.skipped ?? []);
  protected readonly leftOutText = computed(() => this.leftOut().map((x) => `${x.path} (${this.t(('copy.reason.' + x.reason) as MsgKey)})`).join(', '));
  protected readonly kb = computed(() => Math.max(1, Math.round((this.plan()?.bytes ?? 0) / 1024)));
  protected readonly visibility = signal<NewVisibility>('private');

  /** repos other than the source that have no agent of this name, the ones with the source's stack first */
  private readonly candidates = computed(() => {
    const src = this.source();
    const name = this.ref()?.name.toLowerCase();
    if (!src || !name) return [];
    return this.store.repos()
      .filter((r) => r.id !== src.id && !(this.kind() === 'skill' ? r.skills : r.agents).some((a) => a.name.toLowerCase() === name))
      .sort((a, b) => Number(b.stack === src.stack) - Number(a.stack === src.stack) || a.name.localeCompare(b.name))
      .slice(0, MAX_TARGETS);
  });
  protected readonly stackOf = computed(() => new Map(this.store.repos().map((r) => [r.id, r.stack])));
  protected readonly readyTargets = computed(() => this.targets().filter((x) => x.status === 'ready'));
  protected readonly skipped = computed(() => this.targets().filter((x) => x.status !== 'ready' && x.status !== 'cannot-hide'));
  protected readonly unhideable = computed(() => this.targets().filter((x) => x.status === 'cannot-hide'));

  constructor() {
    void this.load();
  }

  protected async setVisibility(v: NewVisibility): Promise<void> {
    if (v === this.visibility() || this.phase() === 'saving') return;
    this.visibility.set(v);
    this.chosen.set(new Set());
    this.phase.set('loading');
    await this.load();
  }

  private async load(): Promise<void> {
    const ref = this.ref();
    const ids = this.candidates().map((r) => r.id);
    if (!ref) return;
    if (ids.length === 0) { this.phase.set('plan'); return; }
    try {
      const plan = await this.store.planCopyAgent(ref.repoId, ref.name, ids, this.visibility(), ref.kind);
      this.plan.set(plan);
      this.targets.set(plan.targets);
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
      this.results.set((await this.store.applyCopyAgent(ref.repoId, ref.name, ids, this.visibility(), ref.kind)).targets);
      this.phase.set('done');
    } catch (e) {
      this.error.set(e instanceof ApiError ? e.message : this.t('copy.saveFailed'));
      this.phase.set('plan');
    }
  }

  protected statusText(r: CopyTarget): string {
    if (r.status === 'cannot-hide') return this.t('copy.status.cannotHide');
    return r.status === 'created' ? this.t(r.hidden ? 'copy.status.createdPrivate' : 'copy.status.created') : this.t('copy.status.skipped');
  }

  protected created(): number {
    return this.results().filter((x) => x.status === 'created').length;
  }
}
