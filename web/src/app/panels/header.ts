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
  protected readonly store = inject(RadarStore);
  private readonly now = signal(new Date());

  /** manual path entry (fallback when there is no native folder dialog) */
  protected readonly editing = signal(false);
  protected readonly draft = signal('');

  protected readonly clock = computed(() => {
    const d = this.now();
    return `${z(d.getHours())}:${z(d.getMinutes())}:${z(d.getSeconds())}`;
  });
  protected readonly isMock = computed(() => this.store.mode() === 'mock');
  protected readonly pathText = computed(() => {
    if (this.isMock()) return this.store.result()?.scanRoot ?? '—';
    return this.store.settings()?.scanPathDisplay || 'Nie wybrano katalogu';
  });
  protected readonly reposCount = computed(() => this.store.result()?.summary.repos ?? 0);
  protected readonly stacksCount = computed(() => this.store.result()?.summary.stacks.length ?? 0);
  protected readonly lastScan = computed(() => formatScanTime(this.store.result()?.scannedAt));
  protected readonly hasResult = computed(() => !!this.store.result());
  protected readonly stateText = computed(() => {
    if (this.store.scanning()) return 'SKANOWANIE…';
    if (!this.store.result()) return 'BRAK SKANU';
    return this.store.staleRoot() ? 'WYNIKI Z INNEGO KATALOGU' : 'SKAN ZAKOŃCZONY';
  });
  protected readonly scanTip = computed(() => {
    if (this.isMock()) return 'Tryb danych przykładowych';
    if (this.store.scanning()) return 'Skan trwa';
    if (!this.store.settings()?.exists) return 'Najpierw wybierz katalog';
    return `Uruchom skan ${this.pathText()}`;
  });

  constructor() {
    const id = setInterval(() => this.now.set(new Date()), 1000);
    inject(DestroyRef).onDestroy(() => clearInterval(id));
  }

  protected async change(): Promise<void> {
    if (this.store.settings()?.canPickFolder && (await this.store.pickFolder())) return;
    this.startEditing();
  }

  protected startEditing(): void {
    this.draft.set(this.store.settings()?.scanPath ?? '');
    this.editing.set(true);
  }

  protected async confirm(): Promise<void> {
    if (await this.store.setScanPath(this.draft())) this.editing.set(false);
  }

  protected cancelEdit(): void {
    this.editing.set(false);
    this.store.notice.set(null);
  }

  protected onDraft(e: Event): void {
    this.draft.set((e.target as HTMLInputElement).value);
  }
}
