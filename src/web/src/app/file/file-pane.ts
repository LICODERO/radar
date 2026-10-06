import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, viewChild } from '@angular/core';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { MdView } from '../shared/md-view';
import { VisibilityTag } from '../shared/visibility-tag';

@Component({
  selector: 'app-file-pane',
  imports: [MdView, VisibilityTag],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './file-pane.html',
  styleUrl: './file-pane.scss'
})
export class FilePane {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  protected readonly f = computed(() => this.store.file()!);
  /** the agent this file is, when it is one of the scanned repo's agents (only those can be copied) */
  protected readonly agent = computed(() => {
    const f = this.store.file();
    const repo = f ? this.store.repos().find((r) => r.id === f.repoId) : undefined;
    return f && repo ? repo.agents.find((a) => a.path === f.path) ?? null : null;
  });
  /** git's view of the open file when it is one of the scanned agents, skills or workflows */
  protected readonly visibility = computed(() => {
    const f = this.store.file();
    const repo = f ? this.store.repos().find((r) => r.id === f.repoId) : undefined;
    if (!f || !repo) return null;
    return repo.agents.find((a) => a.path === f.path)?.visibility
      ?? repo.skills.find((s) => s.path === f.path)?.visibility
      ?? this.store.workflows().flatMap((w) => w.repos).find((x) => x.repoId === f.repoId && x.path === f.path)?.visibility
      ?? null;
  });
  protected readonly canCopy = computed(() => this.store.mode() === 'api' && !!this.agent() && this.f().status === 'ready' && !this.store.scanning());
  protected readonly lines = computed(() => (this.f().text ? this.f().text.split('\n').length : 0));
  protected readonly closeBtn = viewChild<ElementRef<HTMLButtonElement>>('closeBtn');

  constructor() {
    // move focus into the pane when it opens so the keyboard user lands in it; without preventScroll the browser
    // scrolls the (still off-screen, sliding in) pane into view and drags the whole stage sideways
    effect(() => {
      const btn = this.closeBtn();
      if (btn) queueMicrotask(() => btn.nativeElement.focus({ preventScroll: true }));
    });
  }

  protected size(bytes: number): string {
    return bytes < 1024 ? `${bytes} B` : `${(bytes / 1024).toFixed(1)} KB`;
  }
}
