import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ProjectPlan, SkillStatus } from '../core/models';
import { ApiError } from '../core/radar-api';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { MsgKey } from '../i18n/pl';

type Phase = 'loading' | 'plan' | 'saving' | 'done' | 'error';

/** Enables the second brain for the selected repo: shows what would be written, writes only after the user confirms. */
@Component({
  selector: 'app-project-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './project-dialog.html',
  styleUrl: './project-dialog.scss'
})
export class ProjectDialog {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;

  protected readonly phase = signal<Phase>('loading');
  protected readonly plan = signal<ProjectPlan | null>(null);
  protected readonly error = signal<string | null>(null);
  /** let Claude read the vault without a prompt in this repo (on by default: without it every read of the vault asks) */
  protected readonly allowAccess = signal(true);
  protected readonly skill = signal<SkillStatus | null>(null);
  protected readonly skillBusy = signal(false);
  protected readonly access = computed(() => this.plan()?.access ?? null);
  protected readonly accessBlocked = computed(() => this.access()?.blocked ?? null);

  protected readonly repo = computed(() => this.store.repos().find((r) => r.id === this.store.projectRepoId()) ?? null);
  protected readonly creates = computed(() => {
    const root = this.store.vault()?.path ?? '';
    return (this.plan()?.creates ?? []).map((p) => (root && p.startsWith(root) ? p.slice(root.length).replace(/^[\\/]+/, '') : p));
  });

  constructor() {
    void this.load();
    void this.loadSkill();
  }

  private async loadSkill(): Promise<void> {
    try { this.skill.set(await this.store.loadSkill()); } catch { /* the line is simply not shown */ }
  }

  protected async toggleAccess(): Promise<void> {
    if (this.phase() === 'saving') return;
    this.allowAccess.update((v) => !v);
    this.phase.set('loading');
    await this.load();
  }

  protected async installSkill(update: boolean): Promise<void> {
    this.skillBusy.set(true);
    try { this.skill.set(await this.store.installSkill(update)); } catch { /* the state stays as it was */ }
    this.skillBusy.set(false);
  }

  protected blockedText(code: string): string { return this.t(('project.access.blocked.' + code) as MsgKey); }
  protected skillText(): string { return this.t(('vault.skill.state.' + (this.skill()?.state ?? 'not-installed')) as MsgKey); }

  private async load(): Promise<void> {
    const id = this.store.projectRepoId();
    if (!id) return;
    try {
      this.plan.set((await this.store.planProject(id, this.allowAccess())).plan);
      this.phase.set('plan');
    } catch (e) {
      this.error.set(e instanceof ApiError ? e.message : this.t('project.planFailed'));
      this.phase.set('error');
    }
  }

  protected async confirm(): Promise<void> {
    const id = this.store.projectRepoId();
    if (!id || this.phase() !== 'plan') return;
    this.error.set(null);
    this.phase.set('saving');
    try {
      this.plan.set((await this.store.applyProject(id, this.allowAccess())).plan);
      this.phase.set('done');
    } catch (e) {
      this.error.set(e instanceof ApiError ? e.message : this.t('project.saveFailed'));
      this.phase.set('plan');
    }
  }
}
