import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RadarStore } from '../core/radar-store';
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
  protected readonly s = computed(() => this.store.scan()!);
  protected readonly root = computed(() => this.store.settings()?.scanPathDisplay ?? '');
  protected readonly running = computed(() => this.s().status === 'running');
  protected readonly done = computed(() => this.s().status === 'done');
  protected readonly failed = computed(() => this.s().status === 'error');

  protected readonly title = computed(() => this.failed() ? 'BŁĄD SKANU' : this.done() ? 'SKAN ZAKOŃCZONY' : 'SKANOWANIE KATALOGU');
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
    d: `${l.hasClaudeMd ? '✓ CLAUDE.md' : '✕ brak CLAUDE.md'} · ${l.agents} agentów · ${l.skills} skilli`,
    color: l.hasClaudeMd ? '#e2f1f8' : '#ff9cc9'
  })));

  protected readonly cur = computed(() => {
    const s = this.s();
    if (s.status === 'error') return s.message ?? '';
    if (s.status === 'done') {
      return `Zeskanowano ${s.counters.repos} repo · ${s.counters.agents} agentów · ${s.counters.skills} skilli · ${s.workflows} workflows`
        + (s.durationMs !== null ? ` · ${(s.durationMs / 1000).toFixed(1)} s` : '');
    }
    if (s.phase === 'discovery' || s.phase === 'init') return s.found ? `Szukam repozytoriów… znaleziono ${s.found}` : 'Szukam repozytoriów…';
    return s.current ? `${this.root()}/${s.current}/.claude` : '';
  });

  protected readonly pct = computed(() => this.s().percent);
}
