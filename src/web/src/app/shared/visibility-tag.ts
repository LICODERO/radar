import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { Visibility } from '../core/models';
import { I18n } from '../i18n/i18n';
import { MsgKey } from '../i18n/pl';

/** Small label saying whether git shares an agent, skill or workflow (public), hides it (private) or has not decided yet (untracked). Shows nothing when unknown. */
@Component({
  selector: 'app-visibility-tag',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (shown(); as v) {
      <span class="m tag tip tip-b" [class]="v.state + (side() === 'l' ? ' tip-l' : ' tip-r')" [attr.data-tip]="v.tip">{{ v.label }}</span>
    }
  `,
  styleUrl: './visibility-tag.scss'
})
export class VisibilityTag {
  protected readonly t = inject(I18n).t;
  readonly visibility = input<Visibility | null | undefined>(null);
  /** which edge the tooltip hangs from: `r` grows to the left (for tags near the right edge), `l` grows to the right */
  readonly side = input<'l' | 'r'>('r');
  protected readonly shown = computed(() => {
    const v = this.visibility();
    if (v !== 'public' && v !== 'private' && v !== 'untracked') return null;
    return { state: v, label: this.t(('visibility.' + v) as MsgKey), tip: this.t(('visibility.' + v + '.tip') as MsgKey) };
  });
}
