import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RadarStore } from '../core/radar-store';
import { RepoInfo } from '../core/models';
import { I18n } from '../i18n/i18n';

/** The repos that have no CLAUDE.md (the gaps counted in the KPI), opened from the gaps tile. */
@Component({
  selector: 'app-gaps-list-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './gaps-list-dialog.html',
  styleUrl: './gaps-list-dialog.scss'
})
export class GapsListDialog {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  protected readonly gaps = this.store.claudeGaps;

  protected choose(repo: RepoInfo): void {
    this.store.closeGapsList();
    this.store.selectRepo(repo.id);
  }

  protected canCreate(): boolean { return this.store.mode() === 'api'; }

  protected create(repo: RepoInfo): void {
    void this.store.createClaudeMd(repo.id);
  }

  protected generate(): void {
    this.store.closeGapsList();
    void this.store.openGaps();
  }
}
