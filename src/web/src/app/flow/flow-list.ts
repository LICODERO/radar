import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { I18n } from '../i18n/i18n';

export interface FlowListItem {
  id: string;
  title: string;
  sub: string;
  /** small note on the right of the title, e.g. the stack */
  meta?: string;
  subColor?: string;
  disabled?: boolean;
  tip?: string;
  /** can be dragged onto the board */
  drag?: boolean;
}

/** A plain list of two-line buttons (the repos on the left, the rules on the right); an item can also be dragged out of it. */
@Component({
  selector: 'app-flow-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (searchable()) {
      <input class="m q" type="search" autocomplete="off" spellcheck="false" [placeholder]="t('flow.search')" [attr.aria-label]="t('flow.search')" [value]="query()" (input)="query.set($any($event.target).value)" />
    }
    <ul>
      @for (i of shown(); track i.id) {
        <li [attr.draggable]="i.drag && !i.disabled ? 'true' : null" [class.drag]="i.drag && !i.disabled" (dragstart)="dragStart($event, i)">
          <button class="m" type="button" [disabled]="i.disabled" [title]="i.tip ?? ''" (click)="pick.emit(i.id)" (pointerenter)="hover.emit(i.id)" (pointerleave)="hover.emit(null)">
            <span class="rt"><span class="rn">{{ i.title }}</span>@if (i.meta) { <span class="rm">{{ i.meta }}</span> }</span>
            <span class="rs" [style.color]="i.subColor">{{ i.sub }}</span>
          </button>
        </li>
      } @empty {
        <li class="none m">{{ t('flow.noResults') }}</li>
      }
    </ul>
  `,
  styleUrl: './flow-list.scss'
})
export class FlowList {
  protected readonly t = inject(I18n).t;
  readonly items = input.required<FlowListItem[]>();
  readonly searchable = input(false, { transform: (v: unknown) => v === '' || v === true });
  protected readonly query = signal('');
  protected readonly shown = computed(() => {
    const q = this.query().trim().toLowerCase();
    return q ? this.items().filter((i) => i.title.toLowerCase().includes(q) || (i.meta ?? '').toLowerCase().includes(q)) : this.items();
  });
  readonly pick = output<string>();
  readonly hover = output<string | null>();

  protected dragStart(e: DragEvent, i: FlowListItem): void {
    if (!i.drag || i.disabled || !e.dataTransfer) return;
    e.dataTransfer.setData('text/plain', i.id);
    e.dataTransfer.effectAllowed = 'copy';
  }
}
