import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { FLOW_AUTHS, FLOW_KINDS, FlowAuth, FlowKind, FlowRule } from '../core/models';
import { isSync } from './flow-model';
import { I18n } from '../i18n/i18n';
import { MsgKey } from '../i18n/pl';

/** The form of one connection: kind, authentication, the names of the variables that hold secrets, notes and visibility. */
@Component({
  selector: 'app-flow-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './flow-editor.html',
  styleUrl: './flow-editor.scss'
})
export class FlowEditor {
  protected readonly t = inject(I18n).t;
  protected readonly kinds = FLOW_KINDS;
  protected readonly auths = FLOW_AUTHS;

  readonly rule = input.required<FlowRule>();
  readonly envText = input('');
  readonly envBad = input<string[]>([]);
  /** kinds this pair of repos already uses in another rule */
  readonly takenKinds = input<FlowKind[]>([]);

  readonly kind = output<FlowKind>();
  readonly auth = output<FlowAuth>();
  readonly env = output<string>();
  readonly notes = output<string>();
  readonly direction = output<FlowRule['direction']>();
  readonly visibility = output<'public' | 'private'>();
  readonly swap = output<void>();
  readonly remove = output<void>();

  protected readonly directions = ['out', 'in', 'both'] as const;
  protected readonly arrows = { out: '→', in: '←', both: '↔' } as const;
  protected sync(): boolean { return isSync(this.rule().kind); }

  protected dirText(d: FlowRule['direction']): string { return this.t(('flow.dir.' + d) as MsgKey, { from: this.rule().from, to: this.rule().to }); }
  protected kindText(k: FlowKind): string { return this.t(('flow.kind.' + k) as MsgKey); }
  protected authText(a: FlowAuth): string { return this.t(('flow.auth.' + a) as MsgKey); }
}
