import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, viewChild } from '@angular/core';
import { RadarStore } from '../core/radar-store';
import { renderBlocks } from '../core/render-markdown';

@Component({
  selector: 'app-file-pane',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './file-pane.html',
  styleUrl: './file-pane.scss'
})
export class FilePane {
  protected readonly store = inject(RadarStore);
  protected readonly f = computed(() => this.store.file()!);
  protected readonly blocks = computed(() => (this.f().status === 'ready' ? renderBlocks(this.f().text) : []));
  protected readonly lines = computed(() => (this.f().text ? this.f().text.split('\n').length : 0));
  protected readonly closeBtn = viewChild<ElementRef<HTMLButtonElement>>('closeBtn');

  constructor() {
    // move focus into the pane when it opens so the keyboard user lands in it
    effect(() => {
      const btn = this.closeBtn();
      if (btn) queueMicrotask(() => btn.nativeElement.focus());
    });
  }

  protected size(bytes: number): string {
    return bytes < 1024 ? `${bytes} B` : `${(bytes / 1024).toFixed(1)} KB`;
  }
}
