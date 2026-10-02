import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { Pick } from '../core/models';
import { AI_TOOL_ICONS } from '../core/ai-tools';
import { PopFile, Popover } from './orbit-scene';

@Component({
  selector: 'app-orbit',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './orbit.html',
  styleUrl: './orbit.scss'
})
export class Orbit {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  protected readonly scene = this.store.scene;
  protected readonly toolIcon = computed(() => AI_TOOL_ICONS[this.store.aiTool()]);
  protected readonly hasData = computed(() => this.store.repos().length > 0);
  protected readonly avg = computed(() => this.store.result()?.summary.avgCoverage ?? 0);
  /** stroke-dasharray of the coverage arc (circumference of r=66 is ~414.7) */
  protected readonly arc = computed(() => `${((this.avg() / 100) * 414.7).toFixed(1)} 415`);

  /** how far the user dragged the popover from where the scene placed it (stage pixels) */
  protected readonly popOffset = signal({ x: 0, y: 0 });
  private drag: { id: number; x: number; y: number; ox: number; oy: number; scale: number; left: number; top: number } | null = null;

  constructor() {
    // every new pick opens its popover at the scene's own position again
    effect(() => {
      this.store.pick();
      untracked(() => this.popOffset.set({ x: 0, y: 0 }));
    });
  }

  protected dragStart(e: PointerEvent, pop: Popover): void {
    if ((e.target as HTMLElement).closest('button') || e.button !== 0) return;
    const head = e.currentTarget as HTMLElement;
    head.setPointerCapture(e.pointerId);
    const o = this.popOffset();
    // the stage is scaled to fit the window: convert screen pixels to stage pixels
    const scale = head.getBoundingClientRect().width / head.offsetWidth || 1;
    this.drag = { id: e.pointerId, x: e.clientX, y: e.clientY, ox: o.x, oy: o.y, scale, left: pop.left, top: pop.top };
  }

  protected dragMove(e: PointerEvent): void {
    const d = this.drag;
    if (!d || d.id !== e.pointerId) return;
    // keep the popover on the stage (the orbit sits at 360,100 on the 1440x900 stage)
    const x = Math.max(-360 - d.left, Math.min(840 - d.left, d.ox + (e.clientX - d.x) / d.scale));
    const y = Math.max(-100 - d.top, Math.min(780 - d.top, d.oy + (e.clientY - d.y) / d.scale));
    this.popOffset.set({ x, y });
  }

  protected dragEnd(e: PointerEvent): void {
    if (this.drag?.id === e.pointerId) this.drag = null;
  }

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
