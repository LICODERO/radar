import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RadarStore } from '../core/radar-store';
import { coverageColor } from '../core/palette';

@Component({
  selector: 'app-left-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './left-panel.html',
  styleUrl: './left-panel.scss'
})
export class LeftPanel {
  protected readonly store = inject(RadarStore);
  protected readonly color = coverageColor;
  protected readonly total = computed(() => this.store.repos().length);
  protected readonly wfTotal = computed(() => this.store.workflows().length);
  protected readonly gaps = this.store.claudeGaps;
  protected readonly firstGaps = computed(() => this.gaps().slice(0, 2));
  protected readonly restGaps = computed(() => this.gaps().slice(2));
  protected readonly restText = computed(() => {
    const rest = this.restGaps();
    if (!rest.length) return '';
    const names = rest.slice(0, 2).map((r) => `${r.initials} ${r.name}`).join(', ');
    return `+ ${rest.length} kolejne · ${names}${rest.length > 2 ? '…' : ''}`;
  });
  protected readonly genTip = computed(() => {
    const t = this.store.gapTotals();
    return this.store.anyGaps() ? `Polecenia dla luk: ${t['no-claude-md']} bez CLAUDE.md, pozostałe w filtrach` : 'Brak luk do uzupełnienia';
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
