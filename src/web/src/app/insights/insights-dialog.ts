import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { FindingChange, SharedItem } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { formatScanTime } from '../panels/header';
import { MsgKey, PL } from '../i18n/pl';

const MAX_ROWS = 40;

interface FindingRow { key: string; repo: string; path: string; text: string }
interface ChangeRow { key: string; name: string; coverage: string; quality: string | null; items: string | null; up: boolean | null }
interface VariantRow { key: string; label: string; repos: string; count: number; open: () => void }
interface SharedRow { key: string; name: string; kind: 'agent' | 'skill'; drifted: boolean; summary: string; variants: VariantRow[]; missing: string | null }

/**
 * Two views of what is worth coming back for: what changed since the last scan that looked different, and the agents and skills
 * that live in several repos (identical, or drifted apart). Read-only; "open" previews a file like everywhere else.
 */
@Component({
  selector: 'app-insights-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './insights-dialog.html',
  styleUrl: './insights-dialog.scss'
})
export class InsightsDialog {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;

  protected readonly tab = computed(() => this.store.insightsTab() ?? 'changes');
  protected readonly changes = this.store.changes;
  protected readonly since = computed(() => formatScanTime(this.changes()?.previousScannedAt));

  protected readonly deltaCoverage = computed(() => this.delta(this.changes()?.avgCoverageBefore, this.changes()?.avgCoverageAfter));
  protected readonly deltaQuality = computed(() => this.delta(this.changes()?.avgQualityBefore, this.changes()?.avgQualityAfter));

  protected readonly fixed = computed(() => this.findings(this.changes()?.fixed ?? []));
  protected readonly introduced = computed(() => this.findings(this.changes()?.introduced ?? []));
  protected readonly fixedMore = computed(() => Math.max(0, (this.changes()?.fixed.length ?? 0) - MAX_ROWS));
  protected readonly introducedMore = computed(() => Math.max(0, (this.changes()?.introduced.length ?? 0) - MAX_ROWS));

  protected readonly changed = computed<ChangeRow[]>(() => (this.changes()?.changed ?? []).slice(0, MAX_ROWS).map((c) => {
    const items = [c.agentsDelta ? this.t('insights.agentsDelta', { n: sign(c.agentsDelta) }) : '', c.skillsDelta ? this.t('insights.skillsDelta', { n: sign(c.skillsDelta) }) : '']
      .filter(Boolean).join(' · ');
    const up = c.coverageAfter !== c.coverageBefore ? c.coverageAfter > c.coverageBefore
      : c.qualityAfter !== c.qualityBefore ? (c.qualityAfter ?? 0) > (c.qualityBefore ?? 0) : null;
    return {
      key: c.repoId, name: c.name, up,
      coverage: c.coverageBefore === c.coverageAfter ? `${c.coverageAfter}%` : `${c.coverageBefore}% → ${c.coverageAfter}%`,
      quality: c.qualityAfter === null && c.qualityBefore === null ? null
        : c.qualityBefore === c.qualityAfter ? `${c.qualityAfter}%` : `${c.qualityBefore ?? '–'}% → ${c.qualityAfter ?? '–'}%`,
      items: items || null
    };
  }));
  protected readonly changedMore = computed(() => Math.max(0, (this.changes()?.changed.length ?? 0) - MAX_ROWS));

  protected readonly shared = computed<SharedRow[]>(() => this.store.sharedItems().slice(0, MAX_ROWS).map((s) => this.sharedRow(s)));
  protected readonly sharedMore = computed(() => Math.max(0, this.store.sharedItems().length - MAX_ROWS));

  private delta(a: number | null | undefined, b: number | null | undefined): { text: string; cls: string } | null {
    if (a == null || b == null) return null;
    const d = b - a;
    return { text: `${a}% → ${b}%`, cls: d > 0 ? 'up' : d < 0 ? 'down' : 'same' };
  }

  private findings(list: readonly FindingChange[]): FindingRow[] {
    return list.slice(0, MAX_ROWS).map((f, i) => {
      const key = ('quality.' + f.code) as MsgKey;
      return { key: f.repoId + f.path + f.code + i, repo: f.repoName, path: f.path, text: key in PL ? this.t(key, { detail: f.detail ?? '' }) : f.code };
    });
  }

  private sharedRow(s: SharedItem): SharedRow {
    const repos = s.variants.reduce((n, v) => n + v.repos.length, 0);
    const drifted = s.variants.length > 1;
    const letters = 'ABCDEFGHIJ';
    return {
      key: s.kind + s.name, name: s.name, kind: s.kind, drifted,
      summary: drifted ? this.t('insights.sharedDrifted', { repos, n: s.variants.length }) : this.t('insights.sharedSame', { repos }),
      variants: drifted
        ? s.variants.map((v, i) => ({
            key: v.hash, label: this.t('insights.variant', { letter: letters[i] ?? String(i + 1) }), count: v.repos.length,
            repos: v.repos.join(', '), open: () => this.open(v.repos[0], v.path, s.kind)
          }))
        : [],
      missing: s.missing.length ? this.t('insights.missing', { repos: s.missing.slice(0, 6).join(', ') + (s.missing.length > 6 ? ` +${s.missing.length - 6}` : '') }) : null
    };
  }

  protected open(repoId: string, path: string, kind: 'agent' | 'skill'): void {
    this.store.closeInsights();
    void this.store.openFile(repoId, path, path.split('/').pop() ?? path, kind === 'agent' ? '#c6ff3d' : '#a99bff');
  }
}

function sign(n: number): string { return n > 0 ? `+${n}` : String(n); }
