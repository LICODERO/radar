import { ChangeDetectionStrategy, Component, inject, model } from '@angular/core';
import { I18n } from '../i18n/i18n';

export type NewVisibility = 'private' | 'public';

/** Choice for a file the app is about to create: private (kept out of git changes) or public (git sees it). Private is the default. */
@Component({
  selector: 'app-visibility-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="m lbl" id="vis-lbl">{{ t('visibility.pick') }}</div>
    <div class="opts" role="radiogroup" aria-labelledby="vis-lbl">
      @for (o of options; track o) {
        <button type="button" class="m opt" role="radio" [class.on]="value() === o" [attr.aria-checked]="value() === o" [disabled]="disabled()" (click)="value.set(o)">
          <span class="n">{{ o === 'private' ? t('visibility.private') : t('visibility.public') }}</span>
          <span class="d">{{ o === 'private' ? t('visibility.pickPrivate') : t('visibility.pickPublic') }}</span>
        </button>
      }
    </div>
  `,
  styleUrl: './visibility-picker.scss'
})
export class VisibilityPicker {
  protected readonly t = inject(I18n).t;
  protected readonly options: NewVisibility[] = ['private', 'public'];
  readonly value = model<NewVisibility>('private');
  readonly disabled = model(false);
}
