import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { I18n } from '../i18n/i18n';

/** Zoom out, the zoom level, zoom in and "fit all cards" of the flow board. */
@Component({
  selector: 'app-flow-zoom',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button class="m" type="button" [title]="t('flow.zoomOut')" [attr.aria-label]="t('flow.zoomOut')" (click)="zoomOut.emit()">−</button>
    <span class="m pct">{{ pct() }}%</span>
    <button class="m" type="button" [title]="t('flow.zoomIn')" [attr.aria-label]="t('flow.zoomIn')" (click)="zoomIn.emit()">+</button>
    <button class="m fit" type="button" [title]="t('flow.fitTip')" (click)="fit.emit()">{{ t('flow.fit') }}</button>
  `,
  styleUrl: './flow-zoom.scss'
})
export class FlowZoom {
  protected readonly t = inject(I18n).t;
  readonly pct = input.required<number>();
  readonly zoomOut = output<void>();
  readonly zoomIn = output<void>();
  readonly fit = output<void>();
}
