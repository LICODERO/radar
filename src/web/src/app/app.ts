import { ChangeDetectionStrategy, Component, HostListener, computed, inject, signal } from '@angular/core';
import { BASE_H, BASE_W, computeFit } from './core/fit-scale';
import { RadarStore } from './core/radar-store';
import { I18n } from './i18n/i18n';
import { Orbit } from './orbit/orbit';
import { AgentComposer } from './agent/agent-composer';
import { FilePane } from './file/file-pane';
import { GapPanel } from './gaps/gap-panel';
import { AboutDialog } from './about/about-dialog';
import { LegendDialog } from './legend/legend-dialog';
import { GapsListDialog } from './gaps/gaps-list-dialog';
import { RunConfirm } from './gaps/run-confirm';
import { Tour } from './tour/tour';
import { KpiListDialog } from './kpi/kpi-list-dialog';
import { ItemsDialog } from './items/items-dialog';
import { ProjectDialog } from './vault/project-dialog';
import { VaultDialog } from './vault/vault-dialog';
import { CoverageInfo } from './info/coverage-info';
import { ScanOverlay } from './scan/scan-overlay';
import { Header } from './panels/header';
import { LeftPanel } from './panels/left-panel';
import { RightPanel } from './panels/right-panel';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Header, Orbit, LeftPanel, RightPanel, ScanOverlay, FilePane, GapPanel, AgentComposer, CoverageInfo, AboutDialog, LegendDialog, VaultDialog, ProjectDialog, ItemsDialog, GapsListDialog, RunConfirm, KpiListDialog, Tour],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  protected readonly footer = computed(() => this.t('about.footer', { version: this.store.version() ? 'v' + this.store.version() : '' }).trim());
  private readonly size = signal({ w: window.innerWidth, h: window.innerHeight });

  protected readonly fit = computed(() => computeFit(this.size().w, this.size().h));
  protected readonly frameW = computed(() => Math.max(this.size().w, BASE_W * this.fit().scale));
  protected readonly frameH = computed(() => Math.max(this.size().h, BASE_H * this.fit().scale));
  protected readonly left = computed(() => Math.max(0, (this.frameW() - BASE_W * this.fit().scale) / 2));
  protected readonly top = computed(() => Math.max(0, (this.frameH() - BASE_H * this.fit().scale) / 2));
  protected readonly sample = computed(() => this.store.result()?.sample === true);

  constructor() {
    void this.store.init(new URLSearchParams(location.search).get('mock'));
  }

  /** Esc closes the top-most layer: generator, then file preview, then the popover, then the repo selection. */
  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    if (this.store.scan()) return;
    if (this.store.tourVisible()) { this.store.finishTour(); return; }
    if (this.store.pendingRun()) { this.store.cancelRun(); return; }
    if (this.store.legendOpen()) { this.store.closeLegend(); return; }
    if (this.store.projectRepoId()) { this.store.closeProject(); return; }
    if (this.store.kpiList()) { this.store.closeKpiList(); return; }
    if (this.store.itemsKind()) { this.store.closeItems(); return; }
    if (this.store.gapsListOpen()) { this.store.closeGapsList(); return; }
    if (this.store.vaultOpen()) { this.store.closeVault(); return; }
    if (this.store.aboutOpen()) { this.store.closeAbout(); return; }
    if (this.store.coverageInfoOpen()) { this.store.closeCoverageInfo(); return; }
    if (this.store.composerRepoId()) { this.store.closeComposer(); return; }
    if (this.store.gapsOpen()) this.store.closeGaps();
    else if (this.store.file()) this.store.closeFile();
    else if (this.store.pick()) this.store.closePop();
    else if (this.store.selected()) this.store.clearSelection();
  }

  @HostListener('window:resize')
  protected onResize(): void {
    this.size.set({ w: window.innerWidth, h: window.innerHeight });
  }
}
