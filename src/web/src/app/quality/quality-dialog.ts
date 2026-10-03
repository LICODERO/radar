import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { FileQuality, QualityFinding, Severity } from '../core/models';
import { coverageColor } from '../core/palette';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { MsgKey, PL } from '../i18n/pl';

interface FindingRow { key: string; mark: string; severity: Severity; text: string }
interface FileGroup { path: string; kind: FileQuality['kind']; score: number; color: string; findings: FindingRow[] }

const MARK: Record<Severity, string> = { error: '●', warning: '▲', info: '○' };
const RANK: Record<Severity, number> = { error: 0, warning: 1, info: 2 };
const FILE_COLOR: Record<FileQuality['kind'], string> = { 'claude-md': '#c6ff3d', agent: '#c6ff3d', skill: '#a99bff' };

/** What is wrong with the selected repo's existing AI files, worst file first, with a button that has Claude Code fix it. */
@Component({
  selector: 'app-quality-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './quality-dialog.html',
  styleUrl: './quality-dialog.scss'
})
export class QualityDialog {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;

  protected readonly repo = this.store.selected;
  protected readonly canFix = computed(() => this.store.mode() === 'api' && !!this.repo()?.gaps.includes('weak-files') && !this.store.scanning());

  protected readonly groups = computed<FileGroup[]>(() => {
    const q = this.repo()?.quality;
    if (!q) return [];
    return q.files
      .map((f) => ({
        path: f.path, kind: f.kind, score: f.score, color: coverageColor(f.score),
        findings: q.findings
          .filter((x) => x.path === f.path)
          .sort((a, b) => RANK[a.severity] - RANK[b.severity])
          .map((x, i) => this.row(x, i))
      }))
      .filter((g) => g.findings.length > 0)
      .sort((a, b) => a.score - b.score || a.path.localeCompare(b.path));
  });

  private row(f: QualityFinding, i: number): FindingRow {
    const key = ('quality.' + f.code) as MsgKey;
    // a code from a newer server than this UI: show it as it is instead of failing
    const text = key in PL ? this.t(key, { detail: f.detail ?? '' }) : f.code;
    return { key: f.code + i, mark: MARK[f.severity], severity: f.severity, text };
  }

  protected open(g: FileGroup): void {
    const r = this.repo();
    if (!r) return;
    this.store.closeQuality();
    void this.store.openFile(r.id, g.path, g.path.split('/').pop() ?? g.path, FILE_COLOR[g.kind]);
  }
}
