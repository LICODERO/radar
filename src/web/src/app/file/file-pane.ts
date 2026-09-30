import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, viewChild } from '@angular/core';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { MdView } from '../shared/md-view';

@Component({
  selector: 'app-file-pane',
  imports: [MdView],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './file-pane.html',
  styleUrl: './file-pane.scss'
})
export class FilePane {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  protected readonly f = computed(() => this.store.file()!);
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

  protected size(bytes: number): string {
    return bytes < 1024 ? `${bytes} B` : `${(bytes / 1024).toFixed(1)} KB`;
  }
}
