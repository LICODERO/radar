import { ChangeDetectionStrategy, Component, HostListener, computed, inject, signal } from '@angular/core';
import { BASE_H, BASE_W, computeFit } from './core/fit-scale';
import { RadarStore } from './core/radar-store';
import { Orbit } from './orbit/orbit';
import { Header } from './panels/header';
import { LeftPanel } from './panels/left-panel';
import { RightPanel } from './panels/right-panel';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Header, Orbit, LeftPanel, RightPanel],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly store = inject(RadarStore);
  private readonly size = signal({ w: window.innerWidth, h: window.innerHeight });

  protected readonly fit = computed(() => computeFit(this.size().w, this.size().h));
  protected readonly frameW = computed(() => Math.max(this.size().w, BASE_W * this.fit().scale));
  protected readonly frameH = computed(() => Math.max(this.size().h, BASE_H * this.fit().scale));
  protected readonly left = computed(() => Math.max(0, (this.frameW() - BASE_W * this.fit().scale) / 2));
  protected readonly top = computed(() => Math.max(0, (this.frameH() - BASE_H * this.fit().scale) / 2));
  protected readonly sample = computed(() => this.store.result()?.sample === true);

  constructor() {
    const mock = new URLSearchParams(location.search).get('mock');
    this.store.load(mock ? `mock/scan-result-${mock}.json` : 'mock/scan-result.json');
  }

  @HostListener('window:resize')
  protected onResize(): void {
    this.size.set({ w: window.innerWidth, h: window.innerHeight });
  }
}
