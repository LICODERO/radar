import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FlowPlan, FlowRepoPlan } from '../core/models';
import { I18n } from '../i18n/i18n';
import { MsgKey } from '../i18n/pl';

/**
 * What the server will change (or has changed): one collapsed block per repo, in it one row per file, and a row opens to show the
 * text the file gets. Everything starts closed, so the whole picture fits on a screen.
 */
@Component({
  selector: 'app-flow-plan',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './flow-plan.html',
  styleUrl: './flow-plan.scss'
})
export class FlowPlanView {
  protected readonly t = inject(I18n).t;
  readonly plan = input.required<FlowPlan>();
  readonly done = input(false);

  private readonly open = signal<ReadonlySet<string>>(new Set());
  protected readonly files = computed(() => this.plan().repos.reduce((n, r) => n + r.parts.length, 0));
  protected readonly allOpen = computed(() => this.open().size >= this.plan().repos.length + this.files());

  protected isOpen(key: string): boolean { return this.open().has(key); }

  protected toggle(key: string): void {
    this.open.update((s) => { const n = new Set(s); if (!n.delete(key)) n.add(key); return n; });
  }

  protected toggleAll(): void {
    this.open.set(this.allOpen() ? new Set() : new Set(this.plan().repos.flatMap((r) => [r.repo, ...r.parts.map((_, i) => r.repo + '|' + i)])));
  }

  /** "2 created · 1 updated" for the header of a repo */
  protected counts(r: FlowRepoPlan): { action: string; n: number }[] {
    const by = new Map<string, number>();
    for (const p of r.parts) by.set(p.action, (by.get(p.action) ?? 0) + 1);
    return [...by].map(([action, n]) => ({ action, n }));
  }

  protected partTitle(id: string): string { return this.t(('flow.part.' + id) as MsgKey); }
  protected actionText(a: string): string { return this.t(((this.done() ? 'flow.did.' : 'flow.action.') + a) as MsgKey); }
  protected blockedText(p: FlowPlan): string { return this.t(('flow.blocked.' + p.blocked) as MsgKey, { repo: p.blockedRepo ?? '' }); }
  protected noteText(repo: string): string { return this.t('flow.note.no-claude-md', { repo }); }
}
