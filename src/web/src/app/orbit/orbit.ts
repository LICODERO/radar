import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RadarStore } from '../core/radar-store';
import { Pick } from '../core/models';
import { PopFile } from './orbit-scene';

@Component({
  selector: 'app-orbit',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './orbit.html',
  styleUrl: './orbit.scss'
})
export class Orbit {
  protected readonly store = inject(RadarStore);
  protected readonly scene = this.store.scene;
  protected readonly hasData = computed(() => this.store.repos().length > 0);
  protected readonly avg = computed(() => this.store.result()?.summary.avgCoverage ?? 0);
  /** stroke-dasharray of the coverage arc (circumference of r=66 is ~414.7) */
  protected readonly arc = computed(() => `${((this.avg() / 100) * 414.7).toFixed(1)} 415`);

  protected pickAgent(repoId: string, name: string): void { this.store.pickItem({ kind: 'a', repoId, name }); }
  protected pickSkill(repoId: string, name: string): void { this.store.pickItem({ kind: 's', repoId, name }); }
  protected pickWf(id: string): void { this.store.pickItem({ kind: 'w', name: id } satisfies Pick); }
  protected hoverRepo(id: string): void { this.store.setHover({ kind: 'r', name: id }); }
  protected hoverAgent(repoId: string, name: string): void { this.store.setHover({ kind: 'a', repoId, name }); }
  protected hoverSkill(repoId: string, name: string): void { this.store.setHover({ kind: 's', repoId, name }); }
  protected hoverWf(id: string): void { this.store.setHover({ kind: 'w', name: id }); }
  protected openFile(f: PopFile | null): void {
    if (f) void this.store.openFile(f.repoId, f.path, f.kind, f.color);
  }
  protected leave(): void { this.store.setHover(null); }
}
