import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { RadarStore } from '../core/radar-store';

const z = (n: number) => String(n).padStart(2, '0');

export function formatScanTime(iso: string | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return `${z(d.getHours())}:${z(d.getMinutes())} · ${d.getFullYear()}-${z(d.getMonth() + 1)}-${z(d.getDate())}`;
}

@Component({
  selector: 'app-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './header.html',
  styleUrl: './header.scss'
})
export class Header {
  private readonly store = inject(RadarStore);
  private readonly now = signal(new Date());

  protected readonly clock = computed(() => {
    const d = this.now();
    return `${z(d.getHours())}:${z(d.getMinutes())}:${z(d.getSeconds())}`;
  });
  protected readonly root = computed(() => this.store.result()?.scanRoot ?? '—');
  protected readonly reposCount = computed(() => this.store.result()?.summary.repos ?? 0);
  protected readonly stacksCount = computed(() => this.store.result()?.summary.stacks.length ?? 0);
  protected readonly lastScan = computed(() => formatScanTime(this.store.result()?.scannedAt));

  constructor() {
    const id = setInterval(() => this.now.set(new Date()), 1000);
    inject(DestroyRef).onDestroy(() => clearInterval(id));
  }
}
