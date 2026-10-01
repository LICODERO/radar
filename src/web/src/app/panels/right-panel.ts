import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
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
  protected readonly flags = computed(() => {
    const s = this.sel();
    if (!s) return '';
    return this.t('right.flags', {
      claude: s.claudeMd.exists ? '✓' : this.t('right.missing'),
      agents: this.t('right.agentsCount', { n: s.agents.length }),
      skills: this.t('right.skillsCount', { n: s.skills.length })
    });
  });
}
