import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { COVERAGE_HIGH, COVERAGE_MID, COVERAGE_PARTS, COVERAGE_WEIGHTS, CoveragePart } from '../core/coverage';
import { COLOR } from '../core/palette';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { MsgKey } from '../i18n/pl';

@Component({
  selector: 'app-coverage-info',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './coverage-info.html',
  styleUrl: './coverage-info.scss'
})
export class CoverageInfo {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  protected readonly high = COVERAGE_HIGH;
  protected readonly mid = COVERAGE_MID;
  protected readonly midTop = COVERAGE_HIGH - 1;
  protected readonly colors = COLOR;
  protected readonly avg = computed(() => this.store.result()?.summary.avgCoverage ?? 0);
  protected readonly repo = this.store.selected;

  /** the most a repo can earn */
  protected readonly maxScore = COVERAGE_PARTS.reduce((n, p) => n + COVERAGE_WEIGHTS[p], 0);
  /** what the selected repo really earned, or null when no repo is selected (the table only explains the scoring then) */
  protected readonly earnedTotal = computed(() => {
    const parts = this.repo()?.coverage.parts;
    return parts ? COVERAGE_PARTS.reduce((n, p) => n + parts[p], 0) : null;
  });

  /** the scoring parts; when a repo is selected, also what it earned */
  protected readonly rows = computed(() => {
    const parts = this.repo()?.coverage.parts;
    return COVERAGE_PARTS.map((p: CoveragePart) => ({
      key: p,
      weight: COVERAGE_WEIGHTS[p],
      title: this.t(('info.part.' + p) as MsgKey),
      text: this.t(('info.part.' + p + '.text') as MsgKey),
      earned: parts ? parts[p] : null
    }));
  });
}
