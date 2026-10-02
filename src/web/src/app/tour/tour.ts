import { ChangeDetectionStrategy, Component, ElementRef, HostListener, computed, effect, inject, signal, untracked } from '@angular/core';
import { BASE_H, BASE_W } from '../core/fit-scale';
import { RadarStore } from '../core/radar-store';
import { Rect, TOUR_STEPS, placeCard } from '../core/tour';
import { I18n } from '../i18n/i18n';

const CARD_W = 340;
const PAD = 6;

/** The first-run tour: dims the stage, leaves a hole around the highlighted element and puts a card with the explanation beside it. */
@Component({
  selector: 'app-tour',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './tour.html',
  styleUrl: './tour.scss'
})
export class Tour {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly steps = TOUR_STEPS;
  protected readonly step = computed(() => TOUR_STEPS[this.store.tourIndex()] ?? TOUR_STEPS[0]);
  protected readonly last = computed(() => this.store.tourIndex() >= TOUR_STEPS.length - 1);
  /** the highlighted rectangle in stage pixels (null: nothing to highlight, or the element is not on the page) */
  protected readonly hole = signal<Rect | null>(null);
  /** the card's real height (it depends on the text), measured after it is drawn */
  private readonly cardH = signal(230);
  protected readonly pos = computed(() => placeCard(this.hole(), this.step().side, { w: CARD_W, h: this.cardH() }, { w: BASE_W, h: BASE_H }));
  protected readonly cardW = CARD_W;
  protected readonly stageH = BASE_H;
  protected readonly stageW = BASE_W;

  constructor() {
    // measure after the step's DOM is there, and again when the results change the layout
    effect(() => {
      this.store.tourIndex();
      this.store.result();
      untracked(() => setTimeout(() => this.measure(), 0));
    });
  }

  @HostListener('window:resize')
  protected measure(): void {
    const card = this.host.nativeElement.querySelector<HTMLElement>('.card');
    if (card) this.cardH.set(card.offsetHeight);
    const step = this.step();
    if (step.rect) { this.hole.set(step.rect); return; }
    const stage = this.host.nativeElement.closest<HTMLElement>('.stage');
    const el = step.target && stage ? stage.querySelector<HTMLElement>(`[data-tour="${step.target}"]`) : null;
    if (!el || !stage) { this.hole.set(null); return; }
    const scale = stage.getBoundingClientRect().width / BASE_W || 1;
    const r = el.getBoundingClientRect();
    const s = stage.getBoundingClientRect();
    this.hole.set({ x: (r.left - s.left) / scale - PAD, y: (r.top - s.top) / scale - PAD, w: r.width / scale + PAD * 2, h: r.height / scale + PAD * 2 });
  }
}
