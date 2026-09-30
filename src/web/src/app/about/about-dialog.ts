import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ABOUT } from '../core/about';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';

@Component({
  selector: 'app-about-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './about-dialog.html',
  styleUrl: './about-dialog.scss'
})
export class AboutDialog {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  protected readonly about = ABOUT;
}
