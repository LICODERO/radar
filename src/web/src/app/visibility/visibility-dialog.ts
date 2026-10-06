import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { VisibilityOutcome } from '../core/models';
import { ApiError } from '../core/radar-api';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { MsgKey } from '../i18n/pl';
import { VisibilityTag } from '../shared/visibility-tag';

type Phase = 'loading' | 'plan' | 'saving' | 'done' | 'error';

/**
 * Makes one agent, skill or workflow private or public again, opened from its file pane. The server first plans the change (nothing is
 * written), shows exactly what it would do, and only the confirm button applies it. R.A.D.A.R. never commits.
 */
@Component({
  selector: 'app-visibility-dialog',
  imports: [VisibilityTag],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './visibility-dialog.html',
  styleUrl: './visibility-dialog.scss'
})
export class VisibilityDialog {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;

  protected readonly ref = computed(() => this.store.visibilityRef());
  /** a private item can be shared again; everything else can be hidden */
  protected readonly target = computed<'private' | 'public'>(() => (this.ref()?.current === 'private' ? 'public' : 'private'));
  protected readonly phase = signal<Phase>('loading');
  protected readonly error = signal<string | null>(null);
  protected readonly outcome = signal<VisibilityOutcome | null>(null);
  protected readonly actionable = computed(() => {
    const o = this.outcome();
    return !!o && !o.blocked && o.changes.length > 0;
  });
  protected readonly notes = computed(() => this.outcome()?.notes ?? []);

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    const ref = this.ref();
    if (!ref) return;
    try {
      this.outcome.set(await this.store.planVisibility(ref.repoId, ref.path, this.target()));
      this.phase.set('plan');
    } catch (e) {
      this.error.set(e instanceof ApiError ? e.message : this.t('visibility.planFailed'));
      this.phase.set('error');
    }
  }

  protected async confirm(): Promise<void> {
    const ref = this.ref();
    if (!ref || !this.actionable() || this.phase() !== 'plan') return;
    this.error.set(null);
    this.phase.set('saving');
    try {
      const done = await this.store.applyVisibility(ref.repoId, ref.path, this.target());
      this.outcome.set(done);
      this.phase.set(done.blocked ? 'plan' : 'done');
    } catch (e) {
      this.error.set(e instanceof ApiError ? e.message : this.t('visibility.saveFailed'));
      this.phase.set('plan');
    }
  }

  protected changeText(kind: string): string { return this.t(((this.phase() === 'done' ? 'visibility.changed.' : 'visibility.change.') + kind) as MsgKey); }
  protected noteText(code: string): string { return this.t(('visibility.note.' + code) as MsgKey); }
  protected blockedText(code: string): string { return this.t(('visibility.blocked.' + code) as MsgKey); }
}
