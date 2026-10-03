import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject } from '@angular/core';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';

const SHOWN_MS = 8000;

/** One line after a scan the user ran: did the average coverage change, and by how much. Also says so when it did not. */
@Component({
  selector: 'app-coverage-toast',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './coverage-toast.html',
  styleUrl: './coverage-toast.scss'
})
export class CoverageToast {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;

  protected readonly delta = computed(() => {
    const c = this.store.coverageToast();
    return c ? c.to - c.from : 0;
  });
  protected readonly kind = computed(() => (this.delta() > 0 ? 'up' : this.delta() < 0 ? 'down' : 'same'));
  protected readonly text = computed(() => {
    const c = this.store.coverageToast();
    if (!c) return '';
    const d = Math.abs(this.delta());
    switch (this.kind()) {
      case 'up': return this.t('toast.up', { from: c.from, to: c.to, d });
      case 'down': return this.t('toast.down', { from: c.from, to: c.to, d });
      default: return this.t('toast.same', { to: c.to });
    }
  });

  constructor() {
    const id = setTimeout(() => this.store.dismissCoverageToast(), SHOWN_MS);
    inject(DestroyRef).onDestroy(() => clearTimeout(id));
  }
}
