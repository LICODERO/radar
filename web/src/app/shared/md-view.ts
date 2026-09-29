import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { renderBlocks } from '../core/render-markdown';

/** Renders markdown as styled plain-text blocks (see core/render-markdown.ts; never injects HTML). */
@Component({
  selector: 'app-md-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (b of blocks(); track $index) {
      <div [class]="'b ' + b.kind" [style.padding-left.px]="b.kind === 'li' || b.kind === 'ol' ? 6 + b.indent * 16 : null">
        @if (b.kind === 'li') { <span class="dot">•</span> }
        @for (s of b.segs; track $index) {
          @if (s.k === 'b') { <strong>{{ s.t }}</strong> } @else if (s.k === 'c') { <code>{{ s.t }}</code> } @else { {{ s.t }} }
        }
      </div>
    }
  `,
  styleUrl: './md-view.scss'
})
export class MdView {
  readonly text = input.required<string>();
  protected readonly blocks = computed(() => renderBlocks(this.text()));
}
