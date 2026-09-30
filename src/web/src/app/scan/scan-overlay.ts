import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { MsgKey } from '../i18n/pl';
import { coverageColor } from '../core/palette';
import { blipPosition } from '../core/scan-state';

@Component({
  selector: 'app-scan-overlay',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './scan-overlay.html',
  styleUrl: './scan-overlay.scss'
})
export class ScanOverlay {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  protected readonly s = computed(() => this.store.scan()!);
  protected readonly root = computed(() => this.store.settings()?.scanPathDisplay ?? '');
  protected readonly running = computed(() => this.s().status === 'running');
  protected readonly done = computed(() => this.s().status === 'done');
  protected readonly failed = computed(() => this.s().status === 'error');

  protected readonly title = computed(() => this.failed() ? this.t('scan.titleError') : this.done() ? this.t('scan.titleDone') : this.t('scan.titleRunning'));
  protected readonly titleColor = computed(() => this.failed() ? '#ff4fa3' : this.done() ? '#c6ff3d' : '#ffffff');

  protected readonly blips = computed(() => {
    const s = this.s();
    const total = s.total ?? s.blips.length;
    return s.blips.map((b) => {
      const c = coverageColor(b.coverage);
      return { ...blipPosition(b.index, total, b.coverage), color: c === '#ffffff' ? '#c6ff3d' : c };
    });
  });

  protected readonly log = computed(() => this.s().log.map((l) => ({
    t: `${this.root()}/${l.id}`,
    d: this.t('scan.logLine', {
      claude: this.t(l.hasClaudeMd ? 'scan.logClaudeOk' : 'scan.logClaudeMissing'),
      agents: this.t('right.agentsCount', { n: l.agents }),
      skills: this.t('right.skillsCount', { n: l.skills })
    }),
    color: l.hasClaudeMd ? '#e2f1f8' : '#ff9cc9'
  })));

  protected readonly cur = computed(() => {
    const s = this.s();
    if (s.status === 'error') return s.message ?? this.t('scan.unknownError');
    if (s.status === 'done') {
      return this.t('scan.summary', { repos: s.counters.repos, agents: s.counters.agents, skills: s.counters.skills, workflows: s.workflows })
        + (s.durationMs !== null ? ` · ${(s.durationMs / 1000).toFixed(1)} s` : '');
    }
    if (s.phase === 'discovery' || s.phase === 'init') return s.found ? this.t('scan.searchingFound', { n: s.found }) : this.t('scan.searching');
    return s.current ? `${this.root()}/${s.current}/.claude` : '';
  });

  protected readonly phaseLabel = computed(() => this.t(('scan.phase.' + this.s().phase) as MsgKey));
  protected readonly pct = computed(() => this.s().percent);
}
