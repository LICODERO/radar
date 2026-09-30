import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RadarStore } from '../core/radar-store';

export function plural(n: number): string {
  return n + (n === 1 ? ' notatka' : n % 10 >= 2 && n % 10 <= 4 && (n < 10 || n > 20) ? ' notatki' : ' notatek');
}

@Component({
  selector: 'app-right-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './right-panel.html',
  styleUrl: './right-panel.scss'
})
export class RightPanel {
  protected readonly store = inject(RadarStore);
  protected readonly sum = computed(() => this.store.result()?.summary);
  protected readonly sel = this.store.selected;
  protected readonly selName = computed(() => this.store.selected()?.name ?? '—');
  protected readonly canCompose = computed(() => this.store.mode() === 'api' && !!this.store.selected() && !this.store.scanning());
  protected readonly agentTip = computed(() => this.store.mode() === 'mock'
    ? 'Wymaga działającego serwera'
    : !this.store.selected()
      ? 'Najpierw wybierz repozytorium'
      : `Nowy agent w ${this.selName()} z opisu własnymi słowami`);
  protected readonly vault = computed(() => this.store.selected()?.outputs.exists ?? false);

  protected readonly vaultText = computed(() => {
    const s = this.sel();
    if (!s) return '';
    return s.outputs.exists ? `OUTPUTS.md ✓ · ${plural(s.outputs.notes)}` : 'OUTPUTS.md ✕ BRAK';
  });
  protected readonly flags = computed(() => {
    const s = this.sel();
    if (!s) return '';
    return `CLAUDE.md ${s.claudeMd.exists ? '✓' : '✕ BRAK'} · ${s.agents.length} agentów · ${s.skills.length} skilli`;
  });
}
