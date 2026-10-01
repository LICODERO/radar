import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { EnableProjectResult, Hover, Pick, PickKind, RepoInfo, ScanResult, Settings, ToolsInfo, VaultStatus, WorkflowInfo } from './models';
import { buildPager } from './pager';
import { ApiError, RadarApi } from './radar-api';
import { GapItem, Tool, buildCommand, countByType } from './commands';
import { ScanPlayback } from './scan-playback';
import { ScanUiState, applyScanEvent, initialScan } from './scan-state';
import { I18n } from '../i18n/i18n';
import { buildScene } from '../orbit/orbit-scene';

export interface FileView {
  repoId: string;
  repoName: string;
  path: string;
  /** label such as CLAUDE.md, AGENT, SKILL, WORKFLOW */
  kind: string;
  color: string;
  status: 'loading' | 'ready' | 'error';
  text: string;
  truncated: boolean;
  bytes: number;
  error: string | null;
  mode: 'preview' | 'source';
}

/** A run waiting for the user's confirmation; `command` is what will be typed into the terminal (the server builds the real one). */
export interface PendingRun {
  item: GapItem;
  tool: Tool;
  command: string;
  toolFound: boolean;
}

export const REPO_PAGE_SIZE = 8;
export const WF_PAGE_SIZE = 4;

@Injectable({ providedIn: 'root' })
export class RadarStore {
  private readonly http = inject(HttpClient);
  private readonly api = inject(RadarApi);
  private readonly i18n = inject(I18n);
  private events: EventSource | null = null;
  private scanId: string | null = null;
  private fileSeq = 0;
  private readonly playback = new ScanPlayback((name, data) => void this.onEvent(name, data));

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
  readonly file = signal<FileView | null>(null);
  readonly gapsOpen = signal(false);
  /** app version from the server; null until known (and in sample-data mode) */
  readonly version = signal<string | null>(null);
  readonly aboutOpen = signal(false);
  readonly legendOpen = signal(false);
  /** the "how is coverage calculated" dialog */
  readonly coverageInfoOpen = signal(false);
  /** the "new agent from a description" panel; the id of the repo it works on */
  readonly composerRepoId = signal<string | null>(null);
  readonly tools = signal<ToolsInfo | null>(null);
  /** second brain: status from the server (null in sample-data mode or when it could not be read) */
  readonly vault = signal<VaultStatus | null>(null);
  readonly vaultOpen = signal(false);
  /** the full list of the selected repo's agents, skills or workflows (the "+N" tile on its card) */
  readonly itemsKind = signal<PickKind | null>(null);
  /** the list of repos without CLAUDE.md, opened from the gaps tile */
  readonly gapsListOpen = signal(false);
  /** the "enable the second brain for this repo" dialog; the id of the repo */
  readonly projectRepoId = signal<string | null>(null);
  readonly vaultReady = computed(() => this.vault()?.state === 'ok');
  readonly selectedHasVault = computed(() => {
    const id = this.selected()?.id;
    return !!id && !!this.vault()?.projects.some((p) => p.repoId === id);
  });
  readonly gapItems = signal<GapItem[]>([]);
  readonly gapsError = signal<string | null>(null);
  /** run confirmation shown inside the generator; Esc cancels it before it closes the panel */
  readonly pendingRun = signal<PendingRun | null>(null);
  readonly running = signal(false);
  readonly runError = signal<string | null>(null);
  /** repo|gap of the run that was just launched: its RUN button says "opened" for a few seconds */
  readonly launchedKey = signal<string | null>(null);
  /** the terminal app a run opens, for the texts around it */
  readonly terminalName = computed(() => {
    const app = this.tools()?.terminal;
    if (app) return app;
    switch (this.tools()?.platform) {
      case 'macos': return 'Terminal.app';
      case 'windows': return 'PowerShell';
      default: return this.i18n.t('gaps.terminal.generic');
    }
  });
  readonly gapTotals = computed(() => countByType(this.gapItems()));
  readonly anyGaps = computed(() => (this.result()?.repos ?? []).some((r) => r.gaps.length > 0));
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
    return repos.find((r) => r.id === id) ?? null; // nothing is selected until the user picks a repo
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
      pick: this.pick(), hover: this.hover(), matches: this.matches(), tick: this.tick(), lang: this.i18n.lang()
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
      this.version.set(await this.api.version());
      this.settings.set(await this.api.settings());
      this.tools.set(await this.api.toolsInfo());
      this.applyResult(await this.api.latest());
      await this.loadGaps();
      await this.loadVault();
      const running = await this.api.currentScan();
      if (running) await this.attach(running);
    } catch (e) {
      this.error.set(this.messageOf(e, this.i18n.t('store.connect')));
    } finally {
      this.loading.set(false);
    }
  }

  /** A fresh result starts from the default view (no repo selected); `keepSelection` is for quiet refreshes of the same view. */
  private applyResult(r: ScanResult | null, keepSelection = false): void {
    this.result.set(r);
    if (!keepSelection || !r || !r.repos.some((x) => x.id === this.selId())) this.selId.set(null);
    this.pick.set(null);
    this.repoPage.set(0);
    this.wfPage.set(0);
  }

  // ---- read-only file preview -------------------------------------------------------------------
  async openFile(repoId: string, path: string, kind: string, color: string): Promise<void> {
    const repo = this.repos().find((r) => r.id === repoId);
    this.composerRepoId.set(null);
    const seq = ++this.fileSeq;
    const base: FileView = {
      repoId, repoName: repo?.name ?? repoId, path, kind, color, status: 'loading', text: '', truncated: false, bytes: 0,
      error: null, mode: 'preview'
    };
    this.file.set(base);
    if (this.mode() === 'mock') {
      this.file.set({ ...base, status: 'error', error: this.i18n.t('store.fileNeedsServer') });
      return;
    }
    try {
      const f = await this.api.readFile(repoId, path);
      if (seq === this.fileSeq) this.file.set({ ...base, status: 'ready', text: f.content, truncated: f.truncated, bytes: f.bytes });
    } catch (e) {
      if (seq === this.fileSeq) this.file.set({ ...base, status: 'error', error: this.messageOf(e, this.i18n.t('store.fileFailed')) });
    }
  }

  setFileMode(mode: 'preview' | 'source'): void {
    this.file.update((f) => (f ? { ...f, mode } : f));
  }

  closeFile(): void {
    this.fileSeq++;
    this.file.set(null);
  }

  // ---- new agent composer -------------------------------------------------------------------
  openComposer(): void {
    const id = this.selected()?.id;
    if (!id || this.mode() === 'mock') return;
    this.closeFile();
    this.composerRepoId.set(id);
  }

  openLegend(): void { this.legendOpen.set(true); }
  closeLegend(): void { this.legendOpen.set(false); }

  openAbout(): void { this.aboutOpen.set(true); }
  closeAbout(): void { this.aboutOpen.set(false); }

  openCoverageInfo(): void { this.coverageInfoOpen.set(true); }
  closeCoverageInfo(): void { this.coverageInfoOpen.set(false); }

  closeComposer(): void { this.composerRepoId.set(null); }

  // ---- second brain -----------------------------------------------------------------------------
  /** Non-fatal: the rest of the app works without the vault status. */
  async loadVault(): Promise<void> {
    if (this.mode() === 'mock') { this.vault.set(null); return; }
    try { this.vault.set(await this.api.vault()); }
    catch { this.vault.set(null); }
  }

  openVault(): void {
    if (this.mode() === 'mock') return;
    void this.loadVault();
    this.vaultOpen.set(true);
  }
  closeVault(): void { this.vaultOpen.set(false); }

  /** Throws ApiError (with `body.code`) when the server refuses; the dialog shows the message. */
  async createVault(path: string): Promise<void> { this.vault.set(await this.api.createVault(path)); }
  async relinkVault(path: string): Promise<void> { this.vault.set(await this.api.relinkVault(path)); }

  openGapsList(): void { if (this.claudeGaps().length) this.gapsListOpen.set(true); }
  closeGapsList(): void { this.gapsListOpen.set(false); }

  openItems(kind: PickKind): void { if (this.selected()) this.itemsKind.set(kind); }
  closeItems(): void { this.itemsKind.set(null); }

  openProject(): void {
    const id = this.selected()?.id;
    if (!id || this.mode() === 'mock' || !this.vaultReady()) return;
    this.projectRepoId.set(id);
  }
  closeProject(): void { this.projectRepoId.set(null); }

  planProject(repoId: string): Promise<EnableProjectResult> { return this.api.enableProject(repoId, false); }

  async applyProject(repoId: string): Promise<EnableProjectResult> {
    const r = await this.api.enableProject(repoId, true);
    this.vault.set(r.vault);
    return r;
  }

  /**
   * Rescans without the overlay (used after the app created a file) and resolves when the fresh result is in.
   * Does nothing while a normal scan is running.
   */
  async refreshQuiet(): Promise<void> {
    if (this.scanning()) return;
    try {
      const es = await this.api.openEvents(await this.api.startScan());
      await new Promise<void>((resolve) => {
        const done = async () => {
          es.close();
          try {
            this.applyResult(await this.api.latest(), true);
            this.settings.set(await this.api.settings());
            await this.loadGaps();
          } catch { /* the next full scan will catch up */ }
          resolve();
        };
        es.addEventListener('completed', () => void done());
        es.addEventListener('error', (ev) => { if ((ev as MessageEvent).data !== undefined || es.readyState === EventSource.CLOSED) { es.close(); resolve(); } });
        es.addEventListener('cancelled', () => { es.close(); resolve(); });
      });
    } catch { /* best effort */ }
  }

  /** Commands for the gaps come from the server, which owns the prompts. */
  async loadGaps(): Promise<void> {
    if (this.mode() === 'mock' || !this.result()) { this.gapItems.set([]); return; }
    try {
      this.gapItems.set(await this.api.gaps());
      this.gapsError.set(null);
    } catch (e) {
      this.gapItems.set([]);
      this.gapsError.set(this.messageOf(e, this.i18n.t('gaps.fetchFailed')));
    }
  }

  /**
   * "STWÓRZ" in the gaps list: asks for the confirmation to run the CLAUDE.md prompt for this repo in Claude Code, and nothing else
   * (no command panel behind it). Nothing runs without that confirmation; where no terminal can be opened the panel with the
   * commands to copy opens instead.
   */
  async createClaudeMd(repoId: string): Promise<void> {
    this.closeGapsList();
    await this.loadGaps();
    const item = this.gapItems().find((i) => i.repoId === repoId && i.type === 'no-claude-md');
    if (item && this.tools()?.canLaunch === true) this.askRun(item, 'claude');
    else await this.openGaps();
  }

  /** Puts a run up for confirmation (the dialog shows the command that will be typed, by the tool's full path when the server found it). */
  askRun(item: GapItem, tool: Tool): void {
    const tools = this.tools();
    this.runError.set(null);
    this.pendingRun.set({
      item, tool,
      command: buildCommand(tools?.shell ?? 'posix', tool, item.dir, item.prompt, tools?.toolPaths?.[tool]),
      toolFound: tools?.tools[tool] !== false
    });
  }

  cancelRun(): void {
    if (!this.running()) this.pendingRun.set(null);
  }

  /** Opens the terminal for the pending run. The server builds the command; a refusal is shown in the dialog. */
  async confirmRun(): Promise<void> {
    const p = this.pendingRun();
    if (!p || this.running()) return;
    this.running.set(true);
    this.runError.set(null);
    try {
      await this.api.run(p.item.repoId, p.item.type, p.tool);
      const key = p.item.repoId + '|' + p.item.type;
      this.launchedKey.set(key);
      setTimeout(() => { if (this.launchedKey() === key) this.launchedKey.set(null); }, 4000);
      this.pendingRun.set(null);
    } catch (e) {
      this.runError.set(e instanceof ApiError ? e.message : this.i18n.t('gaps.runFailed'));
    } finally {
      this.running.set(false);
    }
  }

  async openGaps(): Promise<void> {
    this.gapsOpen.set(true);
    await this.loadGaps();
  }

  closeGaps(): void {
    this.pendingRun.set(null);
    this.gapsOpen.set(false);
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
      this.notice.set(this.messageOf(e, this.i18n.t('store.pickFailed')));
      return false;
    }
  }

  /** Saves the scan path and, as soon as it is accepted, starts a scan of the new location. */
  async setScanPath(path: string): Promise<boolean> {
    this.notice.set(null);
    try {
      this.settings.set(await this.api.saveScanPath(path));
    } catch (e) {
      this.notice.set(this.messageOf(e, this.i18n.t('store.saveFailed')));
      return false;
    }
    await this.startScan();
    return true;
  }

  // ---- scan ----------------------------------------------------------------------------------
  async startScan(): Promise<void> {
    if (!this.canScan()) return;
    this.notice.set(null);
    try {
      await this.attach(await this.api.startScan());
    } catch (e) {
      this.notice.set(this.messageOf(e, this.i18n.t('store.scanFailed')));
    }
  }

  private async attach(id: string): Promise<void> {
    this.closeStream();
    this.playback.clear();
    this.scanId = id;
    this.scan.set(initialScan());
    const es = await this.api.openEvents(id);
    this.events = es;
    const names = ['started', 'phase', 'repo-found', 'repo-scanned', 'completed', 'cancelled', 'error'];
    for (const name of names) {
      es.addEventListener(name, (ev) => this.playback.push(name, JSON.parse((ev as MessageEvent).data)));
    }
    // A dropped connection would make EventSource retry forever; the server replays history, so just surface it.
    es.onerror = () => {
      if (this.scan()?.status === 'running' && es.readyState === EventSource.CLOSED) {
        this.playback.clear();
        this.scan.update((s) => (s ? applyScanEvent(s, 'error', { message: this.i18n.t('scan.connectionLost') }) : s));
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
        await this.loadGaps();
      } catch (e) {
        this.notice.set(this.messageOf(e, this.i18n.t('store.loadResultFailed')));
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
    if (!id || !this.scanning()) return;
    try {
      await this.api.cancelScan(id);
      // cancelled on the server: drop the queued repos, the stream delivers 'cancelled' next
      this.playback.clear();
      this.closeStream();
      this.scan.set(null);
    } catch {
      // the scan already finished on the server and only the animation is still running: skip to the end
      this.playback.flush();
    }
  }

  /** "ZAMKNIJ I ZOBACZ WYNIKI" / closing an error screen. */
  closeScan(): void {
    this.playback.clear();
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

  /** Selects the repo, or clears the selection when it is already the selected one. */
  toggleRepo(id: string): void {
    if (this.selId() === id) this.clearSelection();
    else this.selectRepo(id);
  }

  clearSelection(): void {
    this.selId.set(null);
    this.pick.set(null);
    this.tick.update((t) => t + 1);
  }

  /**
   * Opens the detail popover of an agent, skill or workflow. An agent or skill selects its repo. A workflow belongs to
   * several repos, so picking one from the orbit or the list drops the repo selection (otherwise the old repo would
   * stay selected next to the repos the workflow is used in); `keepRepo` is for the chips on the selected repo's card.
   */
  pickItem(p: Pick, keepRepo = false): void {
    if (p.kind !== 'w' && p.repoId) this.selId.set(p.repoId);
    else if (p.kind === 'w' && !keepRepo) this.selId.set(null);
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
