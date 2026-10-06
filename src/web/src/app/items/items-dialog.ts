import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Pick, PickKind, Visibility } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { MsgKey } from '../i18n/pl';
import { VisibilityTag } from '../shared/visibility-tag';

interface Row { name: string; text: string; pick: Pick; keepRepo: boolean; visibility?: Visibility | null }

const COLOR: Record<PickKind, string> = { a: '#c6ff3d', s: '#a99bff', w: '#4dd6ff' };

/** Every agent, skill or workflow of the selected repo, for when its card only has room for a few chips. */
@Component({
  selector: 'app-items-dialog',
  imports: [VisibilityTag],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './items-dialog.html',
  styleUrl: './items-dialog.scss'
})
export class ItemsDialog {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;

  protected readonly kind = computed(() => this.store.itemsKind() ?? 'a');
  protected readonly color = computed(() => COLOR[this.kind()]);
  protected readonly repo = this.store.selected;
  protected readonly rows = computed<Row[]>(() => {
    const repo = this.store.selected();
    if (!repo) return [];
    switch (this.kind()) {
      case 'a': return repo.agents.map((a) => ({ name: a.name, text: a.description, pick: { kind: 'a', repoId: repo.id, name: a.name }, keepRepo: false, visibility: a.visibility }));
      case 's': return repo.skills.map((s) => ({ name: s.name, text: s.description, pick: { kind: 's', repoId: repo.id, name: s.name }, keepRepo: false, visibility: s.visibility }));
      default: return this.store.selectedWorkflows().map((w) => ({ name: w.name, text: w.description || w.when, pick: { kind: 'w', name: w.id }, keepRepo: true, visibility: w.repos.find((x) => x.repoId === repo.id)?.visibility }));
    }
  });
  protected readonly eyebrow = computed(() => this.t(('items.eyebrow.' + this.kind()) as MsgKey, { n: this.rows().length }));

  protected choose(row: Row): void {
    this.store.closeItems();
    this.store.pickItem(row.pick, row.keepRepo);
  }
}
