import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { GapType } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { MsgKey } from '../i18n/pl';

/** The confirmation before RADAR opens a terminal and starts claude/codex in a repo. One dialog, whoever asked for the run. */
@Component({
  selector: 'app-run-confirm',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './run-confirm.html',
  styleUrl: './run-confirm.scss'
})
export class RunConfirm {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  protected readonly pending = this.store.pendingRun;
  protected readonly terminal = this.store.terminalName;
  protected readonly toolName = computed(() => (this.pending()?.tool === 'codex' ? 'Codex CLI' : 'Claude Code'));
  protected readonly label = (g: GapType): string => this.t(('gap.' + g) as MsgKey);
}
