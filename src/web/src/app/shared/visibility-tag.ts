import { ChangeDetectionStrategy, Component, booleanAttribute, computed, inject, input } from '@angular/core';
import { Visibility } from '../core/models';
import { I18n } from '../i18n/i18n';
import { MsgKey } from '../i18n/pl';

/** Small label saying whether git shares an agent, skill or workflow (public), hides it (private) or has not decided yet (untracked). Shows nothing when unknown. */
@Component({
  selector: 'app-visibility-tag',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (shown(); as v) {
      @if (compact()) {
        <span class="ico tip tip-b" [class]="v.state + (side() === 'l' ? ' tip-l' : ' tip-r')" role="img" [attr.aria-label]="v.label" [attr.data-tip]="v.label + ' – ' + v.tip">
          <svg viewBox="0 0 24 24" aria-hidden="true">
            @switch (v.state) {
              @case ('private') { <path d="M18 8h-1V6c0-2.76-2.24-5-5-5S7 3.24 7 6v2H6c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V10c0-1.1-.9-2-2-2zm-6 9c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2zm3.1-9H8.9V6c0-1.71 1.39-3.1 3.1-3.1 1.71 0 3.1 1.39 3.1 3.1v2z" /> }
              @case ('public') { <path d="M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5c-1.66 0-3 1.34-3 3s1.34 3 3 3zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5C6.34 5 5 6.34 5 8s1.34 3 3 3zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5c0-2.33-4.67-3.5-7-3.5zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5z" /> }
              @default { <path d="M12 7c2.76 0 5 2.24 5 5 0 .65-.13 1.26-.36 1.83l2.92 2.92c1.51-1.26 2.7-2.89 3.43-4.75-1.73-4.39-6-7.5-11-7.5-1.4 0-2.74.25-3.98.7l2.16 2.16C10.74 7.13 11.35 7 12 7zM2 4.27l2.28 2.28.46.46C3.08 8.3 1.78 10.02 1 12c1.73 4.39 6 7.5 11 7.5 1.55 0 3.03-.3 4.38-.84l.42.42L19.73 22 21 20.73 3.27 3 2 4.27zM7.53 9.8l1.55 1.55c-.05.21-.08.43-.08.65 0 1.66 1.34 3 3 3 .22 0 .44-.03.65-.08l1.55 1.55c-.67.33-1.41.53-2.2.53-2.76 0-5-2.24-5-5 0-.79.2-1.53.53-2.2zm4.31-.78l3.15 3.15.02-.16c0-1.66-1.34-3-3-3l-.17.01z" /> }
            }
          </svg>
        </span>
      } @else {
      <span class="m tag tip tip-b" [class]="v.state + (side() === 'l' ? ' tip-l' : ' tip-r')" [attr.data-tip]="v.tip">{{ v.label }}</span>
      }
    }
  `,
  styleUrl: './visibility-tag.scss'
})
export class VisibilityTag {
  protected readonly t = inject(I18n).t;
  readonly visibility = input<Visibility | null | undefined>(null);
  /** which edge the tooltip hangs from: `r` grows to the left (for tags near the right edge), `l` grows to the right */
  readonly side = input<'l' | 'r'>('r');
  /** an icon (lock, people, crossed-out eye: git does not watch it) instead of the word, for tight places; the tooltip then carries the word too */
  readonly compact = input(false, { transform: booleanAttribute });
  protected readonly shown = computed(() => {
    const v = this.visibility();
    if (v !== 'public' && v !== 'private' && v !== 'untracked') return null;
    return { state: v, label: this.t(('visibility.' + v) as MsgKey), tip: this.t(('visibility.' + v + '.tip') as MsgKey) };
  });
}
