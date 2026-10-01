import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { SkillStatus } from '../core/models';
import { ApiError, RadarApi } from '../core/radar-api';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { MsgKey } from '../i18n/pl';

/** intro → location (create) for a new vault; manage for a working one; relink when the folder was moved or is not a vault */
type View = 'intro' | 'location' | 'manage' | 'relink';

const SUBFOLDER = 'RADAR Second Brain';

@Component({
  selector: 'app-vault-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './vault-dialog.html',
  styleUrl: './vault-dialog.scss'
})
export class VaultDialog {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  private readonly api = inject(RadarApi);

  protected readonly view = signal<View>(this.initialView());
  protected readonly path = signal(this.initialPath());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly errorCode = signal<string | null>(null);
  protected readonly justCreated = signal(false);

  protected readonly skill = signal<SkillStatus | null>(null);
  protected readonly skillBusy = signal(false);
  protected readonly skillError = signal<string | null>(null);

  protected readonly vault = this.store.vault;
  protected readonly canPick = computed(() => !!this.store.settings()?.canPickFolder);
  protected readonly subPath = computed(() => this.path().trim().replace(/[\\/]+$/, '') + '/' + SUBFOLDER);
  protected readonly relinkReason = computed(() => {
    const v = this.vault();
    return v?.state === 'missing' && v.reason ? this.t(('vault.relink.' + v.reason) as MsgKey, { path: v.pathDisplay }) : null;
  });
  protected readonly skillState = computed(() => {
    const s = this.skill();
    return s ? this.t(('vault.skill.state.' + s.state) as MsgKey) : '';
  });
  protected readonly canInstall = computed(() => {
    const s = this.skill()?.state;
    return s === 'not-installed' || s === 'outdated';
  });

  constructor() {
    void this.loadSkill();
  }

  private initialView(): View {
    const state = this.store.vault()?.state;
    return state === 'ok' ? 'manage' : state === 'missing' ? 'relink' : 'intro';
  }

  private initialPath(): string {
    const v = this.store.vault();
    return v?.state === 'missing' && v.path ? v.path : (v?.suggestedPath ?? '');
  }

  protected onPath(e: Event): void {
    this.path.set((e.target as HTMLInputElement).value);
    this.error.set(null);
    this.errorCode.set(null);
  }

  protected go(view: View): void {
    this.error.set(null);
    this.errorCode.set(null);
    this.view.set(view);
  }

  protected toLocation(): void {
    this.path.set(this.vault()?.suggestedPath ?? this.path());
    this.go('location');
  }

  protected toRelink(): void {
    this.path.set(this.vault()?.path ?? this.path());
    this.go('relink');
  }

  /** Native folder dialog (macOS / Windows); a cancelled or unavailable dialog changes nothing. */
  protected async pick(): Promise<void> {
    try {
      const picked = await this.api.pickFolder();
      if (picked) { this.path.set(picked); this.error.set(null); this.errorCode.set(null); }
    } catch { /* manual entry still works */ }
  }

  protected async create(path = this.path()): Promise<void> {
    if (!path.trim() || this.busy()) return;
    await this.run(() => this.store.createVault(path.trim()), this.t('vault.createFailed'), () => {
      this.justCreated.set(true);
      this.view.set('manage');
    });
  }

  protected useSubfolder(): void {
    const sub = this.subPath(); // read before the path changes: it is derived from it
    this.path.set(sub);
    void this.create(sub);
  }

  protected async relink(): Promise<void> {
    if (!this.path().trim() || this.busy()) return;
    await this.run(() => this.store.relinkVault(this.path().trim()), this.t('vault.relink.failed'), () => {
      this.justCreated.set(false);
      this.view.set('manage');
    });
  }

  private async run(action: () => Promise<void>, fallback: string, done: () => void): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.errorCode.set(null);
    try {
      await action();
      done();
    } catch (e) {
      this.error.set(e instanceof ApiError ? e.message : fallback);
      this.errorCode.set(e instanceof ApiError ? (e.body?.code ?? null) : null);
    } finally {
      this.busy.set(false);
    }
  }

  protected async loadSkill(): Promise<void> {
    try { this.skill.set(await this.api.skill()); this.skillError.set(null); }
    catch (e) { this.skillError.set(e instanceof ApiError ? e.message : this.t('vault.skill.loadFailed')); }
  }

  protected async installSkill(): Promise<void> {
    const current = this.skill();
    if (!current || !this.canInstall() || this.skillBusy()) return;
    this.skillBusy.set(true);
    this.skillError.set(null);
    try {
      this.skill.set(await this.api.installSkill(current.state === 'outdated'));
    } catch (e) {
      await this.loadSkill(); // the state may have changed (for instance the file was edited by hand)
      this.skillError.set(e instanceof ApiError ? e.message : this.t('vault.skill.failed'));
    } finally {
      this.skillBusy.set(false);
    }
  }
}
