import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { limitChips } from '../core/chips';
import { coverageColor } from '../core/palette';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';

@Component({
  selector: 'app-right-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './right-panel.html',
  styleUrl: './right-panel.scss'
})
export class RightPanel {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  protected readonly sum = computed(() => this.store.result()?.summary);
  protected readonly sel = this.store.selected;
  protected readonly selName = computed(() => this.store.selected()?.name ?? '—');
  protected readonly canCompose = computed(() => this.store.mode() === 'api' && !!this.store.selected() && !this.store.scanning());
  protected readonly agentTip = computed(() => this.store.mode() === 'mock'
    ? this.t('right.needServer')
    : !this.store.selected()
      ? this.t('right.pickRepoFirst')
      : this.t('right.newAgentTip', { name: this.selName() }));
  protected itemTip(kind: 'skill' | 'workflow'): string {
    return this.store.mode() === 'mock'
      ? this.t('right.needServer')
      : !this.store.selected()
        ? this.t('right.pickRepoFirst')
        : this.t(kind === 'skill' ? 'right.newSkillTip' : 'right.newWorkflowTip', { name: this.selName() });
  }
  protected readonly agentChips = computed(() => limitChips(this.sel()?.agents ?? []));
  protected readonly skillChips = computed(() => limitChips(this.sel()?.skills ?? []));
  protected readonly workflowChips = computed(() => limitChips(this.store.selectedWorkflows()));
  protected readonly isMock = computed(() => this.store.mode() === 'mock');
  protected readonly vaultState = computed(() => this.store.vault()?.state ?? 'none');
  protected readonly vaultTip = computed(() => {
    const v = this.store.vault();
    if (this.isMock()) return this.t('right.needServer');
    return v?.state === 'ok' ? this.t('right.vaultTip.ok', { path: v.pathDisplay })
      : v?.state === 'missing' ? this.t('right.vaultTip.missing')
      : this.t('right.vaultTip.none');
  });
  protected readonly canEnable = computed(() => !this.isMock() && this.store.vaultReady() && !!this.store.selected() && !this.store.scanning());
  protected readonly enableTip = computed(() =>
    this.isMock() ? this.t('right.needServer')
      : !this.store.vaultReady() ? this.t('right.enableRepoNeedVault')
      : !this.store.selected() ? this.t('right.pickRepoFirst')
      : this.t('right.enableRepoTip', { name: this.selName() }));
  protected readonly qualityColor = computed(() => coverageColor(this.sel()?.quality?.score ?? 100));
  protected readonly qualityText = computed(() => {
    const q = this.sel()?.quality;
    if (q?.score == null) return '';
    return q.findings.length > 0
      ? this.t('right.quality', { score: q.score, n: q.findings.length })
      : this.t('right.qualityClean', { score: q.score });
  });
  protected readonly qualityTip = computed(() => `${this.t('right.qualityLabel')} ${this.qualityText()}. ${this.t('right.qualityTip')}`);
  /** the text of the CLAUDE.md link: its name, or the words that it is missing */
  protected readonly claudeLabel = computed(() => (this.sel()?.claudeMd.exists ? 'CLAUDE.md' : this.t('right.noClaudeMd')));
  /** the rest of the flags line, after the CLAUDE.md link */
  protected readonly flagsRest = computed(() => {
    const s = this.sel();
    if (!s) return '';
    const counts = `${this.t('right.agentsCount', { n: s.agents.length })} · ${this.t('right.skillsCount', { n: s.skills.length })}`;
    return s.claudeMd.exists ? ` ✓ · ${counts}` : ` · ${counts}`;
  });
}
