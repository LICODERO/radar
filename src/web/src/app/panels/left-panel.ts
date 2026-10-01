import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { coverageColor } from '../core/palette';

@Component({
  selector: 'app-left-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './left-panel.html',
  styleUrl: './left-panel.scss'
})
export class LeftPanel {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  protected readonly color = coverageColor;
  protected readonly total = computed(() => this.store.repos().length);
  protected readonly wfTotal = computed(() => this.store.workflows().length);
  protected readonly genTip = computed(() => {
    const t = this.store.gapTotals();
    return this.store.anyGaps() ? this.t('left.generateTip', { n: t['no-claude-md'] }) : this.t('left.noGaps');
  });
  protected onQuery(e: Event): void {
    this.store.setQuery((e.target as HTMLInputElement).value);
  }
  protected hoverRepo(id: string): void { this.store.setHover({ kind: 'r', name: id }); }
  protected hoverWf(id: string): void { this.store.setHover({ kind: 'w', name: id }); }
  protected leave(): void { this.store.setHover(null); }
  protected chain(agents: string[], description: string): string {
    return agents.length ? agents.join(' → ') : description;
  }
}
