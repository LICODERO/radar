import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject } from '@angular/core';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';

const SHOWN_MS = 12000;

/** One line under the header after a scan: what got fixed, what is new, how the coverage moved. Click opens the details. */
@Component({
  selector: 'app-changes-toast',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './changes-toast.html',
  styleUrl: './changes-toast.scss'
})
export class ChangesToast {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;

  protected readonly text = computed(() => {
    const c = this.store.changesToast();
    if (!c) return '';
    const parts: string[] = [];
    if (c.fixed.length) parts.push(this.t('toast.fixed', { n: c.fixed.length }));
    if (c.introduced.length) parts.push(this.t('toast.introduced', { n: c.introduced.length }));
    if (c.newRepos.length) parts.push(this.t('toast.newRepos', { n: c.newRepos.length }));
    if (c.avgCoverageBefore !== c.avgCoverageAfter) parts.push(this.t('toast.coverage', { from: c.avgCoverageBefore, to: c.avgCoverageAfter }));
    return parts.length ? parts.join(' · ') : this.t('toast.generic');
  });
  protected readonly bad = computed(() => (this.store.changesToast()?.introduced.length ?? 0) > 0);

  constructor() {
    const id = setTimeout(() => this.store.dismissChangesToast(), SHOWN_MS);
    inject(DestroyRef).onDestroy(() => clearTimeout(id));
  }

  protected open(): void {
    this.store.dismissChangesToast();
    this.store.openInsights('changes');
  }
}
