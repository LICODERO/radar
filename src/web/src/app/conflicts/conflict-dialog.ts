import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Shell } from '../core/commands';
import { cdCommand, removalCommand, repoDir } from '../core/conflict';
import { ConflictItem, NameConflict } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { MsgKey } from '../i18n/pl';
import { VisibilityTag } from '../shared/visibility-tag';

const KIND_COLOR: Record<NameConflict['kind'], string> = { agent: '#c6ff3d', skill: '#a99bff', workflow: '#4dd6ff' };
const KIND_LABEL: Record<NameConflict['kind'], string> = { agent: 'AGENT', skill: 'SKILL', workflow: 'WORKFLOW' };

/**
 * A shared file and a local one with the same name in one repo: Claude sees both and may take the wrong one, and git refuses to pull when
 * the shared file reaches the local one's path. The dialog asks which one to keep and shows the command that removes the others; the app
 * never deletes anything itself.
 */
@Component({
  selector: 'app-conflict-dialog',
  imports: [VisibilityTag],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './conflict-dialog.html',
  styleUrl: './conflict-dialog.scss'
})
export class ConflictDialog {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;

  protected readonly repo = computed(() => this.store.repos().find((r) => r.id === this.store.conflictRepoId()) ?? null);
  protected readonly conflicts = computed(() => this.repo()?.nameConflicts ?? []);
  /** the file the user chose to keep, per conflict (`kind:name`) */
  protected readonly keep = signal<Readonly<Record<string, string>>>({});
  protected readonly copied = signal<string | null>(null);
  private readonly shell = computed<Shell>(() => this.store.tools()?.shell ?? 'posix');

  protected key(c: NameConflict): string { return `${c.kind}:${c.name}`; }
  protected label(c: NameConflict): string { return KIND_LABEL[c.kind]; }
  protected color(c: NameConflict): string { return KIND_COLOR[c.kind]; }

  protected choose(c: NameConflict, item: ConflictItem): void {
    const k = this.key(c);
    this.keep.update((m) => ({ ...m, [k]: m[k] === item.path ? '' : item.path }));
    this.copied.set(null);
  }

  protected kept(c: NameConflict): string | null { return this.keep()[this.key(c)] || null; }

  /** the items that go when `kept` stays */
  protected removed(c: NameConflict): ConflictItem[] {
    const kept = this.kept(c);
    return kept ? c.items.filter((i) => i.path !== kept) : [];
  }

  /** cd + one removal command per item, as the lines the user pastes */
  protected script(c: NameConflict): string {
    const repo = this.repo();
    const root = this.store.result()?.scanRoot;
    const lines = this.removed(c).map((i) => removalCommand(this.shell(), c.kind, i));
    return (repo && root ? [cdCommand(this.shell(), repoDir(root, repo.path, this.shell())), ...lines] : lines).join('\n');
  }

  protected hasShared(c: NameConflict): boolean { return this.removed(c).some((i) => i.visibility === 'public'); }

  protected async copy(c: NameConflict): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.script(c));
      this.copied.set(this.key(c));
    } catch { /* clipboard not available: the text can still be selected by hand */ }
  }

  protected open(c: NameConflict, item: ConflictItem): void {
    const repo = this.repo();
    if (!repo) return;
    this.store.closeConflicts();
    void this.store.openFile(repo.id, item.path, this.label(c), this.color(c));
  }

  protected text(key: string, params?: Record<string, string | number>): string { return this.t(key as MsgKey, params); }
}
