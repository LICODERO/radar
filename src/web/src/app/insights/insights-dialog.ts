import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { SharedItem } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';

const MAX_ROWS = 40;

interface VariantRow { key: string; label: string; repos: string; count: number; open: () => void }
interface SharedRow { key: string; name: string; kind: 'agent' | 'skill'; drifted: boolean; summary: string; variants: VariantRow[]; missing: string | null }

/**
 * Agents and skills that live in several repos, identical or drifted apart (read-only; "open" previews a file like everywhere else).
 * Copying an agent to other repos is done from the agent's file pane.
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

  protected readonly shared = computed<SharedRow[]>(() => this.store.sharedItems().slice(0, MAX_ROWS).map((s) => this.sharedRow(s)));
  protected readonly sharedMore = computed(() => Math.max(0, this.store.sharedItems().length - MAX_ROWS));

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
