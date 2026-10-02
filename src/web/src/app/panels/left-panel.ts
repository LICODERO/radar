import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { coverageColor } from '../core/palette';
import { AiTools } from './ai-tools';

@Component({
  selector: 'app-left-panel',
  imports: [AiTools],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './left-panel.html',
  styleUrl: './left-panel.scss'
})
export class LeftPanel {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  protected readonly color = coverageColor;
  protected readonly total = computed(() => this.store.repos().length);
  protected onQuery(e: Event): void {
    this.store.setQuery((e.target as HTMLInputElement).value);
  }
  protected hoverRepo(id: string): void { this.store.setHover({ kind: 'r', name: id }); }
  protected leave(): void { this.store.setHover(null); }
}
