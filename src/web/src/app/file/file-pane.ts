import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, viewChild } from '@angular/core';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { MsgKey } from '../i18n/pl';
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
  /** the state to show, or null when git could not tell (nothing is shown then) */
  protected readonly shownVisibility = computed(() => {
    const v = this.visibility();
    return v === 'public' || v === 'private' || v === 'untracked' ? v : null;
  });
  protected readonly visibilityTip = computed(() => {
    const v = this.shownVisibility();
    return v ? `${this.t(('visibility.' + v) as MsgKey)} – ${this.t(('visibility.' + v + '.tip') as MsgKey)} ${this.t('visibility.changeHint')}` : '';
  });
  protected readonly canChangeVisibility = computed(() => {
    const v = this.visibility();
    return this.store.mode() === 'api' && !this.store.scanning() && (v === 'public' || v === 'private' || v === 'untracked');
  });
  /** the skill this file is (its SKILL.md), when it is one of the scanned repo's skills */
  protected readonly skill = computed(() => {
    const f = this.store.file();
    const repo = f ? this.store.repos().find((r) => r.id === f.repoId) : undefined;
    return f && repo ? repo.skills.find((s) => s.path === f.path) ?? null : null;
  });
  protected readonly canCopy = computed(() => this.store.mode() === 'api' && (!!this.agent() || !!this.skill()) && this.f().status === 'ready' && !this.store.scanning());
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

  protected copy(): void {
    const f = this.store.file();
    if (!f) return;
    const a = this.agent();
    const s = this.skill();
    if (a) this.store.openCopyAgent(f.repoId, a.name);
    else if (s) this.store.openCopyAgent(f.repoId, s.name, 'skill');
  }

  protected changeVisibility(): void {
    const f = this.store.file();
    const v = this.visibility();
    if (f && v) this.store.openVisibility(f.repoId, f.path, v);
  }

  protected size(bytes: number): string {
    return bytes < 1024 ? `${bytes} B` : `${(bytes / 1024).toFixed(1)} KB`;
  }
}
