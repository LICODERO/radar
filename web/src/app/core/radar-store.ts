import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Hover, Pick, RepoInfo, ScanResult, Settings, WorkflowInfo } from './models';
import { buildPager } from './pager';
import { ApiError, RadarApi } from './radar-api';
import { ScanUiState, applyScanEvent, initialScan } from './scan-state';
import { buildScene } from '../orbit/orbit-scene';

export const REPO_PAGE_SIZE = 10;
export const WF_PAGE_SIZE = 3;

@Injectable({ providedIn: 'root' })
export class RadarStore {
  private readonly http = inject(HttpClient);
  private readonly api = inject(RadarApi);
  private events: EventSource | null = null;
  private scanId: string | null = null;

  readonly result = signal<ScanResult | null>(null);
  readonly error = signal<string | null>(null);
  readonly selId = signal<string | null>(null);
  readonly hover = signal<Hover | null>(null);
  readonly pick = signal<Pick | null>(null);
  readonly tick = signal(0);
  readonly repoPage = signal(0);
  readonly wfPage = signal(0);
  readonly query = signal('');

  /** 'api' = real server, 'mock' = static sample data (?mock=...) */
  readonly mode = signal<'api' | 'mock'>('api');
  readonly loading = signal(true);
  readonly settings = signal<Settings | null>(null);
  readonly notice = signal<string | null>(null);
  readonly scan = signal<ScanUiState | null>(null);
  readonly scanning = computed(() => this.scan()?.status === 'running');
  readonly canScan = computed(() => this.mode() === 'api' && !!this.settings()?.exists && !this.scanning());
  readonly staleRoot = computed(() => {
    const r = this.result();
    const cfg = this.settings()?.scanPath;
    return !!r && !r.sample && !!cfg && r.scanRoot !== cfg;
  });

  readonly repos = computed<RepoInfo[]>(() => this.result()?.repos ?? []);
  readonly workflows = computed<WorkflowInfo[]>(() => this.result()?.workflows ?? []);

  readonly selected = computed<RepoInfo | null>(() => {
    const repos = this.repos();
    const id = this.selId();
    return repos.find((r) => r.id === id) ?? repos[0] ?? null;
  });

  /** repos matching the search box (name, stack, agent name); null when the box is empty */
  readonly matches = computed<ReadonlySet<string> | null>(() => {
    const q = this.query().trim().toLowerCase();
    if (!q) return null;
    return new Set(
      this.repos()
        .filter((r) => r.name.toLowerCase().includes(q) || r.stack.toLowerCase().includes(q)
          || r.agents.some((a) => a.name.toLowerCase().includes(q)))
        .map((r) => r.id)
    );
  });

  readonly filteredRepos = computed(() => {
    const m = this.matches();
    return m ? this.repos().filter((r) => m.has(r.id)) : this.repos();
  });

  readonly repoPager = computed(() => buildPager(this.filteredRepos().length, REPO_PAGE_SIZE, this.repoPage()));
  readonly pagedRepos = computed(() => {
    const p = this.repoPager();
    return this.filteredRepos().slice(p.page * REPO_PAGE_SIZE, p.page * REPO_PAGE_SIZE + REPO_PAGE_SIZE);
  });

  readonly wfPager = computed(() => buildPager(this.workflows().length, WF_PAGE_SIZE, this.wfPage()));
  readonly pagedWorkflows = computed(() => {
    const p = this.wfPager();
    return this.workflows().slice(p.page * WF_PAGE_SIZE, p.page * WF_PAGE_SIZE + WF_PAGE_SIZE);
  });

  readonly selectedWorkflows = computed(() => {
    const s = this.selected();
    return s ? this.workflows().filter((w) => w.repos.some((x) => x.repoId === s.id)) : [];
  });

  readonly claudeGaps = computed(() => {
    const byId = new Map(this.repos().map((r) => [r.id, r] as const));
    return (this.result()?.gaps ?? [])
      .filter((g) => g.type === 'no-claude-md')
      .map((g) => byId.get(g.repoId))
      .filter((r): r is RepoInfo => !!r);
  });

  readonly scene = computed(() =>
    buildScene({
      repos: this.repos(), workflows: this.workflows(), selId: this.selected()?.id ?? null,
      pick: this.pick(), hover: this.hover(), matches: this.matches(), tick: this.tick()
    })
  );

  /** Start-up: sample data when ?mock=... is present, otherwise settings + last saved scan from the server. */
  async init(mock: string | null): Promise<void> {
    try {
      if (mock !== null) {
        this.mode.set('mock');
        const url = mock ? `mock/scan-result-${mock}.json` : 'mock/scan-result.json';
        this.result.set(await firstValueFrom(this.http.get<ScanResult>(url)));
        return;
      }
      this.settings.set(await this.api.settings());
      this.applyResult(await this.api.latest());
      const running = await this.api.currentScan();
      if (running) await this.attach(running);
    } catch (e) {
      this.error.set(this.messageOf(e, 'Nie można połączyć się z serwerem R.A.D.A.R. Uruchom go poleceniem ./run.sh.'));
    } finally {
      this.loading.set(false);
    }
  }

  private applyResult(r: ScanResult | null): void {
    this.result.set(r);
    if (r && !r.repos.some((x) => x.id === this.selId())) this.selId.set(r.repos[0]?.id ?? null);
    this.pick.set(null);
    this.repoPage.set(0);
    this.wfPage.set(0);
  }

  // ---- scan path -----------------------------------------------------------------------------
  /** Native folder dialog; returns false when it is unavailable and the UI should offer manual entry. */
  async pickFolder(): Promise<boolean> {
    this.notice.set(null);
    try {
      const path = await this.api.pickFolder();
      if (path) await this.setScanPath(path);
      return true;
    } catch (e) {
      if (e instanceof ApiError && e.status === 501) return false;
      this.notice.set(this.messageOf(e, 'Nie udało się otworzyć okna wyboru katalogu.'));
      return false;
    }
  }

  async setScanPath(path: string): Promise<boolean> {
    this.notice.set(null);
    try {
      this.settings.set(await this.api.saveScanPath(path));
      return true;
    } catch (e) {
      this.notice.set(this.messageOf(e, 'Nie udało się zapisać ścieżki.'));
      return false;
    }
  }

  // ---- scan ----------------------------------------------------------------------------------
  async startScan(): Promise<void> {
    if (!this.canScan()) return;
    this.notice.set(null);
    try {
      await this.attach(await this.api.startScan());
    } catch (e) {
      this.notice.set(this.messageOf(e, 'Nie udało się uruchomić skanu.'));
    }
  }

  private async attach(id: string): Promise<void> {
    this.closeStream();
    this.scanId = id;
    this.scan.set(initialScan());
    const es = await this.api.openEvents(id);
    this.events = es;
    const names = ['started', 'phase', 'repo-found', 'repo-scanned', 'completed', 'cancelled', 'error'];
    for (const name of names) {
      es.addEventListener(name, (ev) => void this.onEvent(name, JSON.parse((ev as MessageEvent).data)));
    }
    // A dropped connection would make EventSource retry forever; the server replays history, so just surface it.
    es.onerror = () => {
      if (this.scan()?.status === 'running' && es.readyState === EventSource.CLOSED) {
        this.scan.update((s) => (s ? applyScanEvent(s, 'error', { message: 'Utracono połączenie z serwerem.' }) : s));
        this.closeStream();
      }
    };
  }

  private async onEvent(name: string, data: unknown): Promise<void> {
    const prev = this.scan();
    if (!prev) return;
    // "started" is replayed on re-attach; keep what we have unless this is a fresh state
    this.scan.set(applyScanEvent(prev, name, data));
    if (name === 'completed') {
      this.closeStream();
      try {
        this.applyResult(await this.api.latest());
        this.settings.set(await this.api.settings());
      } catch (e) {
        this.notice.set(this.messageOf(e, 'Skan się zakończył, ale nie udało się wczytać wyniku.'));
      }
    } else if (name === 'cancelled') {
      this.closeStream();
      this.scan.set(null); // as in the mockup: cancelling closes the overlay and keeps the previous results
    } else if (name === 'error') {
      this.closeStream();
    }
  }

  async cancelScan(): Promise<void> {
    const id = this.scanId;
    if (id && this.scanning()) {
      try { await this.api.cancelScan(id); } catch { /* the stream reports the outcome */ }
    }
  }

  /** "ZAMKNIJ I ZOBACZ WYNIKI" / closing an error screen. */
  closeScan(): void {
    this.closeStream();
    this.scan.set(null);
  }

  private closeStream(): void {
    this.events?.close();
    this.events = null;
  }

  private messageOf(e: unknown, fallback: string): string {
    return e instanceof ApiError ? e.message : fallback;
  }

  selectRepo(id: string): void {
    this.selId.set(id);
    this.pick.set(null);
    this.tick.update((t) => t + 1);
  }

  pickItem(p: Pick): void {
    if (p.kind !== 'w' && p.repoId) this.selId.set(p.repoId);
    this.pick.set(p);
    this.tick.update((t) => t + 1);
  }

  closePop(): void {
    this.pick.set(null);
  }

  setHover(h: Hover | null): void {
    this.hover.set(h);
  }

  setQuery(q: string): void {
    this.query.set(q);
    this.repoPage.set(0);
  }

  goRepoPage(p: number): void {
    this.repoPage.set(buildPager(this.filteredRepos().length, REPO_PAGE_SIZE, p).page);
  }

  goWfPage(p: number): void {
    this.wfPage.set(buildPager(this.workflows().length, WF_PAGE_SIZE, p).page);
  }
}
