import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { LocalPart, LocalPlan } from '../core/models';
import { ApiError } from '../core/radar-api';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { MsgKey } from '../i18n/pl';

type Phase = 'loading' | 'plan' | 'saving' | 'done' | 'error';

/**
 * Sets up what tells Claude about a repo's private setup: an index of private workflows and the AGENTS.md import in CLAUDE.local.md
 * (which is kept out of git), and, when the user ticks it, the public workflows in CLAUDE.md. The server first plans (nothing is
 * written); only the confirm button writes.
 */
@Component({
  selector: 'app-local-file-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './local-file-dialog.html',
  styleUrl: './local-file-dialog.scss'
})
export class LocalFileDialog {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;

  protected readonly repo = computed(() => this.store.repos().find((r) => r.id === this.store.localFileRepoId()) ?? null);
  protected readonly phase = signal<Phase>('loading');
  protected readonly error = signal<string | null>(null);
  protected readonly plan = signal<LocalPlan | null>(null);
  protected readonly includePublic = signal(false);
  protected readonly actionable = computed(() => {
    const p = this.plan();
    return !!p && !p.blocked && p.parts.length > 0;
  });
  /** the checkbox is only worth showing when there is something public to list (or it is already ticked) */
  protected readonly canOfferPublic = computed(() => (this.plan()?.publicCandidates ?? 0) > 0 || this.includePublic());

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    const id = this.store.localFileRepoId();
    if (!id) return;
    this.phase.set('loading');
    try {
      this.plan.set(await this.store.planLocalFile(id, this.includePublic()));
      this.phase.set('plan');
    } catch (e) {
      this.error.set(e instanceof ApiError ? e.message : this.t('local.planFailed'));
      this.phase.set('error');
    }
  }

  protected async togglePublic(): Promise<void> {
    if (this.phase() === 'saving') return;
    this.includePublic.update((v) => !v);
    await this.load();
  }

  protected async confirm(): Promise<void> {
    const id = this.store.localFileRepoId();
    if (!id || !this.actionable() || this.phase() !== 'plan') return;
    this.error.set(null);
    this.phase.set('saving');
    try {
      this.plan.set(await this.store.applyLocalFile(id, this.includePublic()));
      this.phase.set('done');
    } catch (e) {
      this.error.set(e instanceof ApiError ? e.message : this.t('local.saveFailed'));
      this.phase.set('plan');
    }
  }

  protected partTitle(p: LocalPart): string { return this.t(('local.part.' + p.id) as MsgKey); }
  protected actionText(p: LocalPart): string { return this.t(((this.phase() === 'done' ? 'local.did.' : 'local.action.') + p.action) as MsgKey); }
  protected blockedText(code: string): string { return this.t(('local.blocked.' + code) as MsgKey); }
  protected noteText(code: string): string { return this.t(('local.note.' + code) as MsgKey); }
}
