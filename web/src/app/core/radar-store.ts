import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Hover, Pick, RepoInfo, ScanResult, WorkflowInfo } from './models';
import { buildPager } from './pager';
import { buildScene } from '../orbit/orbit-scene';

export const REPO_PAGE_SIZE = 10;
export const WF_PAGE_SIZE = 3;

@Injectable({ providedIn: 'root' })
export class RadarStore {
  private readonly http = inject(HttpClient);

  readonly result = signal<ScanResult | null>(null);
  readonly error = signal<string | null>(null);
  readonly selId = signal<string | null>(null);
  readonly hover = signal<Hover | null>(null);
  readonly pick = signal<Pick | null>(null);
  readonly tick = signal(0);
  readonly repoPage = signal(0);
  readonly wfPage = signal(0);
  readonly query = signal('');

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

  load(url: string): void {
    this.http.get<ScanResult>(url).subscribe({
      next: (r) => { this.result.set(r); this.error.set(null); },
      error: (e) => this.error.set(`Nie udało się wczytać wyniku skanu (${e?.status ?? '?'})`)
    });
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
