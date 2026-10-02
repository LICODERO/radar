import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { coverageColor } from '../core/palette';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';

interface Row { key: string; name: string; sub: string; side: string; sideColor: string | null; go: () => void }

const ACCENT = { repos: '#e6e9f2', agents: '#c6ff3d', skills: '#a99bff' } as const;

/** Everything behind the repos, agents or skills tile: all repos, or every agent / skill of every repo. A row selects the repo, or opens the agent / skill. */
@Component({
  selector: 'app-kpi-list-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './kpi-list-dialog.html',
  styleUrl: './kpi-list-dialog.scss'
})
export class KpiListDialog {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;

  protected readonly kind = computed(() => this.store.kpiList() ?? 'repos');
  protected readonly accent = computed(() => ACCENT[this.kind()]);
  protected readonly rows = computed<Row[]>(() => {
    const kind = this.kind();
    const repos = this.store.repos();
    if (kind === 'repos') {
      return repos.map((r) => ({
        key: r.id, name: `${r.initials} ${r.name}`, sub: `${r.stack} · A${r.agents.length} · S${r.skills.length}`,
        side: `${r.coverage.score}%`, sideColor: coverageColor(r.coverage.score), go: () => this.store.selectRepo(r.id)
      }));
    }
    const pick = kind === 'agents' ? 'a' : 's';
    return repos
      .flatMap((r) => (kind === 'agents' ? r.agents : r.skills).map((x) => ({
        key: `${r.id}/${x.name}`, name: x.name, sub: x.description, side: r.name, sideColor: null,
        go: () => this.store.pickItem({ kind: pick, repoId: r.id, name: x.name })
      })))
      .sort((a, b) => a.name.localeCompare(b.name) || a.side.localeCompare(b.side));
  });
  protected readonly eyebrow = computed(() => {
    const n = this.rows().length;
    switch (this.kind()) {
      case 'repos': return this.t('kpiList.repos', { n });
      case 'agents': return this.t('kpiList.agents', { n });
      default: return this.t('kpiList.skills', { n });
    }
  });
  protected readonly hint = computed(() => {
    switch (this.kind()) {
      case 'repos': return this.t('kpiList.hint.repos');
      case 'agents': return this.t('kpiList.hint.agents');
      default: return this.t('kpiList.hint.skills');
    }
  });

  protected choose(row: Row): void {
    this.store.closeKpiList();
    row.go();
  }
}
