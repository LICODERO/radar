import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { COLOR } from '../core/palette';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';

@Component({
  selector: 'app-legend-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './legend-dialog.html',
  styleUrl: './legend-dialog.scss'
})
export class LegendDialog {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  protected readonly c = COLOR;
}
