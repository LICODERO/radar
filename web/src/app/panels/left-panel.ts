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
  protected readonly selName = computed(() => this.store.selected()?.name ?? '—');
  protected readonly vault = computed(() => this.store.selected()?.outputs.exists ?? false);

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
