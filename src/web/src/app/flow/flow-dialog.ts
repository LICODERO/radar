import { ChangeDetectionStrategy, Component, ElementRef, HostListener, computed, effect, inject, signal } from '@angular/core';
import { FlowAuth, FlowKind, FlowPlan, FlowRule } from '../core/models';
import { ApiError } from '../core/radar-api';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { MsgKey } from '../i18n/pl';
import { FlowEditor } from './flow-editor';
import { FlowList, FlowListItem } from './flow-list';
import { FlowPlanView } from './flow-plan';
import { FlowZoom } from './flow-zoom';
import { CARD_H, CARD_W, PortPick, Pos, SIDES, Side, View, away, clampPos, curve, facing, fitView, freeKind, freeSpot, isSync, loadLayout, loadPorts, newRule, parseEnv, portPos, ruleKey, sameRules, saveLayout, savePorts, zoomAt } from './flow-model';

type Phase = 'loading' | 'edit' | 'planning' | 'plan' | 'saving' | 'done' | 'error';

const KIND_COLOR: Record<FlowKind, string> = {
  rest: '#4dd6ff', graphql: '#ff4fa3', grpc: '#a99bff', events: '#c6ff3d', db: '#ffb84d', files: '#e6e9f2', other: '#8a90a8'
};

interface EdgeView { start: boolean; end: boolean; key: string; rule: FlowRule; d: string; color: string; mid: Pos; label: string; selected: boolean; hot: boolean; dim: boolean }

/**
 * The wizard for the flow between repos: repos are cards on a board you can zoom and move around, a connection is dragged from the
 * right dot (out) of the caller to the called repo (its left dot, in) and described on the right (kind, authentication, names of the
 * variables that hold secrets, notes, visibility). Nothing is written until the user has seen the plan and confirms it; the server
 * writes the same rules into both repos and puts a pointer in CLAUDE.md or CLAUDE.local.md, so an agent working in one repo knows it
 * has to look at the other.
 */
@Component({
  selector: 'app-flow-dialog',
  imports: [FlowEditor, FlowList, FlowPlanView, FlowZoom],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './flow-dialog.html',
  styleUrl: './flow-dialog.scss'
})
export class FlowDialog {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly cardW = CARD_W;
  protected readonly cardH = CARD_H;
  protected readonly sides = SIDES;

  protected readonly phase = signal<Phase>('loading');
  protected readonly error = signal<string | null>(null);
  protected readonly plan = signal<FlowPlan | null>(null);

  protected readonly rules = signal<FlowRule[]>([]);
  private readonly saved = signal<FlowRule[]>([]);
  protected readonly onBoard = signal<string[]>([]);
  protected readonly pos = signal<Record<string, Pos>>({});
  protected readonly view = signal<View>({ x: 20, y: 20, k: 1 });
  private readonly ports = signal<Record<string, PortPick>>(loadPorts());
  protected readonly selectedKey = signal<string | null>(null);
  protected readonly linking = signal<{ name: string; side: Side } | null>(null);
  protected readonly linkPoint = signal<Pos | null>(null);
  protected readonly hoverKey = signal<string | null>(null);
  protected readonly hoverCard = signal<string | null>(null);
  protected readonly panning = signal(false);
  protected readonly envText = signal('');
  protected readonly envBad = signal<string[]>([]);
  protected readonly notice = signal<string | null>(null);

  private drag: { name: string; dx: number; dy: number; moved: boolean } | null = null;
  private pan: { x: number; y: number; vx: number; vy: number } | null = null;
  private pressed = false;

  protected readonly dirty = computed(() => !sameRules(this.rules(), this.saved()));
  protected readonly selected = computed(() => this.rules().find((r) => ruleKey(r) === this.selectedKey()) ?? null);
  protected readonly takenKinds = computed<FlowKind[]>(() => {
    const cur = this.selected();
    return cur ? this.rules().filter((r) => r !== cur && r.from === cur.from && r.to === cur.to).map((r) => r.kind) : [];
  });
  protected readonly zoomPct = computed(() => Math.round(this.view().k * 100));
  protected readonly duplicates = computed(() => {
    const seen = new Set<string>();
    const dup = new Set<string>();
    for (const r of this.store.repos()) (seen.has(r.name) ? dup : seen).add(r.name);
    return dup;
  });
  private readonly byName = computed(() => new Map(this.store.repos().map((r) => [r.name, r] as const)));
  protected readonly kindColors = KIND_COLOR;
  protected readonly kindList = Object.keys(KIND_COLOR) as FlowKind[];
  protected readonly sidebar = computed(() => this.store.repos().slice().sort((a, b) => a.name.localeCompare(b.name)));
  protected readonly repoItems = computed<FlowListItem[]>(() => this.sidebar().map((r) => {
    const placed = this.onBoard().includes(r.name);
    const dup = this.duplicates().has(r.name);
    return { id: r.name, title: r.name, sub: placed ? '✓ ' + this.t('flow.onCanvas') : '+ ' + this.t('flow.addToBoard'), subColor: placed ? undefined : '#4dd6ff', meta: this.stackOf(r), disabled: placed || dup, drag: true, tip: dup ? this.t('flow.duplicateName') : this.t('flow.reposHint') };
  }));
  /** the stack when the scanner recognised one ("Inne" only means it did not) */
  protected stackOf(r?: { stack: string }): string { return r && r.stack && r.stack !== 'Inne' ? r.stack : ''; }

  protected readonly ruleItems = computed<FlowListItem[]>(() => this.edges().map((e) => ({ id: e.key, title: e.rule.from + ' → ' + e.rule.to, sub: e.label, subColor: e.color })));
  protected readonly cards = computed(() => this.onBoard().map((name) => ({ name, repo: this.byName().get(name), pos: this.pos()[name] ?? { x: 0, y: 0 } })));
  protected readonly worldTransform = computed(() => { const v = this.view(); return `translate(${v.x}px, ${v.y}px) scale(${v.k})`; });

  protected readonly edges = computed<EdgeView[]>(() => {
    const p = this.pos();
    const hk = this.hoverKey();
    const hc = this.hoverCard();
    return this.rules().flatMap((rule) => {
      const a = p[rule.from];
      const b = p[rule.to];
      if (!a || !b) return [];
      const key = ruleKey(rule);
      const pick = this.ports()[key] ?? { a: facing(a, b), b: facing(b, a) };
      const dir = isSync(rule.kind) ? 'out' : rule.direction;
      // the arrow heads stop at the edge of the dots, not under them
      const start = dir !== 'out';
      const end = dir !== 'in';
      const c = curve(away(portPos(a, pick.a), pick.a, start ? 9 : 0), pick.a, away(portPos(b, pick.b), pick.b, end ? 9 : 0), pick.b);
      const hot = key === hk || (!!hc && (rule.from === hc || rule.to === hc));
      return [{
        key, rule, start, end, d: c.d, color: KIND_COLOR[rule.kind], mid: c.mid,
        label: this.t(('flow.kind.' + rule.kind) as MsgKey), selected: key === this.selectedKey(), hot, dim: (!!hk || !!hc) && !hot
      }];
    });
  });

  /** the curve being dragged from a dot to the pointer */
  protected readonly draft = computed(() => {
    const l = this.linking();
    const to = this.linkPoint();
    const a = l ? this.pos()[l.name] : null;
    if (!l || !to || !a) return null;
    return curve(portPos(a, l.side), l.side, to, null).d;
  });

  constructor() {
    effect(() => this.store.flowDirty.set(this.dirty()));
    void this.load();
  }

  private async load(): Promise<void> {
    this.phase.set('loading');
    try {
      const rules = await this.store.flowRules();
      this.rules.set(rules);
      this.saved.set(rules);
      const names = [...new Set(rules.flatMap((r) => [r.from, r.to]))];
      this.onBoard.set(names);
      this.arrange(names);
      this.phase.set('edit');
      const focus = this.store.flowFocus();
      if (focus) this.add(focus);
      setTimeout(() => this.fit());
    } catch (e) {
      this.error.set(e instanceof ApiError ? e.message : this.t('flow.loadFailed'));
      this.phase.set('error');
    }
  }

  /** a remembered position where there is one, a free slot for the rest */
  private arrange(names: string[]): void {
    const remembered = loadLayout('default');
    const layout: Record<string, Pos> = { ...this.pos() };
    for (const n of names) layout[n] ??= remembered[n] ?? freeSpot(Object.values(layout));
    this.pos.set(layout);
  }

  // ---- board ------------------------------------------------------------------------------------
  /** puts a repo on the board: where it was dropped, or on the next free spot when it was clicked */
  protected add(name: string, at?: Pos): void {
    if (this.duplicates().has(name) || this.onBoard().includes(name) || !this.byName().has(name)) return;
    this.onBoard.update((l) => [...l, name]);
    if (at) this.pos.update((p) => ({ ...p, [name]: clampPos({ x: at.x - CARD_W / 2, y: at.y - CARD_H / 2 }) }));
    else this.arrange([name]);
    this.remember();
  }

  protected onDragOver(e: DragEvent): void {
    e.preventDefault();
    if (e.dataTransfer) e.dataTransfer.dropEffect = 'copy';
  }

  protected onDrop(e: DragEvent): void {
    e.preventDefault();
    const name = e.dataTransfer?.getData('text/plain');
    if (name) this.add(name, this.world(e));
  }

  protected removeCard(name: string, e: Event): void {
    e.stopPropagation();
    this.rules.update((l) => l.filter((r) => r.from !== name && r.to !== name));
    this.onBoard.update((l) => l.filter((n) => n !== name));
    this.hoverCard.set(null);
    if (this.selectedKey() && !this.selected()) this.selectedKey.set(null);
    if (this.linking()?.name === name) this.cancelLink();
  }

  private remember(): void { saveLayout('default', this.pos()); }

  private stageScale(): number {
    const w = this.host.nativeElement.getBoundingClientRect().width;
    return w > 0 ? w / 1440 : 1;
  }

  private boardEl(): HTMLElement | null { return this.host.nativeElement.querySelector<HTMLElement>('.board'); }

  /** the pointer in the board's own (unscaled stage) pixels */
  private local(e: { clientX: number; clientY: number }): Pos {
    const rect = this.boardEl()!.getBoundingClientRect();
    const s = this.stageScale();
    return { x: (e.clientX - rect.left) / s, y: (e.clientY - rect.top) / s };
  }

  /** the pointer in world coordinates (where the cards live) */
  private world(e: { clientX: number; clientY: number }): Pos {
    const l = this.local(e);
    const v = this.view();
    return { x: (l.x - v.x) / v.k, y: (l.y - v.y) / v.k };
  }

  protected fit(): void {
    const el = this.boardEl();
    if (!el) return;
    const s = this.stageScale();
    const r = el.getBoundingClientRect();
    this.view.set(fitView(this.onBoard().map((n) => this.pos()[n]).filter(Boolean), r.width / s, r.height / s));
  }

  protected zoomBy(factor: number): void {
    const el = this.boardEl();
    if (!el) return;
    const s = this.stageScale();
    const r = el.getBoundingClientRect();
    this.view.update((v) => zoomAt(v, factor, r.width / s / 2, r.height / s / 2));
  }

  protected onWheel(e: WheelEvent): void {
    e.preventDefault();
    const l = this.local(e);
    this.view.update((v) => zoomAt(v, Math.exp(-e.deltaY * 0.0015), l.x, l.y));
  }

  /** the empty board is dragged to move around; a click on it only clears the current pick */
  protected startPan(e: PointerEvent): void {
    if (e.button !== 0 || (e.target as HTMLElement).closest('[data-card],.tag')) return;
    const v = this.view();
    this.pan = { x: e.clientX, y: e.clientY, vx: v.x, vy: v.y };
    this.panning.set(true);
    this.pressed = true;
  }

  protected startDrag(name: string, e: PointerEvent): void {
    if (e.button !== 0) return;
    const p = this.world(e);
    const at = this.pos()[name];
    this.drag = { name, dx: p.x - at.x, dy: p.y - at.y, moved: false };
    this.pressed = true;
    e.preventDefault();
  }

  protected startLink(name: string, side: Side, e: PointerEvent): void {
    if (e.button !== 0) return;
    e.stopPropagation();
    e.preventDefault();
    this.notice.set(null);
    this.linking.set({ name, side });
    this.linkPoint.set(this.world(e));
    this.pressed = true;
  }

  @HostListener('document:pointermove', ['$event'])
  protected onMove(e: PointerEvent): void {
    if (this.drag) {
      const p = this.world(e);
      this.drag.moved = true;
      const next = clampPos({ x: p.x - this.drag.dx, y: p.y - this.drag.dy });
      this.pos.update((l) => ({ ...l, [this.drag!.name]: next }));
    } else if (this.pan) {
      const s = this.stageScale();
      this.view.update((v) => ({ ...v, x: this.pan!.vx + (e.clientX - this.pan!.x) / s, y: this.pan!.vy + (e.clientY - this.pan!.y) / s }));
    } else if (this.linking() && this.pressed) {
      this.linkPoint.set(this.world(e));
    }
  }

  @HostListener('document:pointerup', ['$event'])
  protected onUp(e: PointerEvent): void {
    const l = this.linking();
    if (this.drag) {
      if (this.drag.moved) this.remember();
      this.drag = null;
    } else if (this.pan) {
      this.pan = null;
      this.panning.set(false);
    } else if (l && this.pressed) {
      const hit = document.elementFromPoint(e.clientX, e.clientY) as HTMLElement | null;
      const target = hit?.closest<HTMLElement>('[data-card]')?.dataset['card'];
      const dot = hit?.closest<HTMLElement>('[data-port]')?.dataset['port'] as Side | undefined;
      if (target && target !== l.name) this.connect(l, target, dot);
      // released on its own card: stay in link mode so a click on the other card finishes it; on nothing: give up
      else if (target === l.name) this.linkPoint.set(null);
      else this.cancelLink();
    }
    this.pressed = false;
  }

  /** a click on a card finishes a connection started with a click on a dot */
  protected cardClick(name: string): void {
    const l = this.linking();
    if (l && l.name !== name && !this.pressed) this.connect(l, name);
  }

  protected cancelLink(): void { this.linking.set(null); this.linkPoint.set(null); }

  /** `dot`: the dot of the other card it was dropped on; dropped anywhere else on the card, the side that faces the first one is used */
  private connect(l: { name: string; side: Side }, other: string, dot?: Side): void {
    this.cancelLink();
    const kind = freeKind(this.rules(), l.name, other);
    if (!kind) { this.notice.set(this.t('flow.noFreeKind')); return; }
    const rule = newRule(l.name, other, kind);
    const key = ruleKey(rule);
    const a = this.pos()[l.name];
    const b = this.pos()[other];
    this.setPorts(key, { a: l.side, b: dot ?? facing(b, a) });
    this.rules.update((list) => [...list, rule]);
    this.select(key);
  }

  private setPorts(key: string, pick: PortPick | null): void {
    this.ports.update((p) => { const n = { ...p }; if (pick) n[key] = pick; else delete n[key]; return n; });
    savePorts(this.ports());
  }

  // ---- editor -----------------------------------------------------------------------------------
  protected select(key: string | null): void {
    this.selectedKey.set(key);
    const r = this.rules().find((x) => ruleKey(x) === key);
    this.envText.set(r?.authEnv.join(', ') ?? '');
    this.envBad.set([]);
    this.cancelLink();
  }

  private patch(change: Partial<FlowRule>): void {
    const cur = this.selected();
    if (!cur) return;
    const next = { ...cur, ...change };
    this.rules.update((l) => l.map((r) => (r === cur ? next : r)));
    this.moveKey(ruleKey(cur), ruleKey(next));
    this.selectedKey.set(ruleKey(next));
  }

  /** a rule that changes its kind keeps the dots its line used */
  private moveKey(from: string, to: string): void {
    const pick = this.ports()[from];
    if (from !== to && pick) { this.setPorts(to, pick); this.setPorts(from, null); }
  }

  /** turns the connection around: the other repo becomes the caller (a wrong drag is fixed here) */
  protected swap(): void {
    const cur = this.selected();
    if (!cur) return;
    const next = { ...cur, from: cur.to, to: cur.from, direction: cur.direction === 'out' ? 'in' as const : cur.direction === 'in' ? 'out' as const : cur.direction };
    if (isSync(cur.kind)) next.direction = 'out';
    if (this.rules().some((r) => r !== cur && ruleKey(r) === ruleKey(next))) { this.notice.set(this.t('flow.noFreeKind')); return; }
    const pick = this.ports()[ruleKey(cur)];
    this.rules.update((l) => l.map((r) => (r === cur ? next : r)));
    if (pick) { this.setPorts(ruleKey(next), { a: pick.b, b: pick.a }); this.setPorts(ruleKey(cur), null); }
    this.select(ruleKey(next));
  }

  protected setKind(kind: FlowKind): void {
    if (this.takenKinds().includes(kind)) return;
    const change: Partial<FlowRule> = { kind };
    if (isSync(kind)) change.direction = 'out';
    this.patch(change);
  }
  protected setAuth(auth: FlowAuth): void {
    this.patch(auth === 'none' ? { auth, authEnv: [] } : { auth });
    if (auth === 'none') { this.envText.set(''); this.envBad.set([]); }
  }

  protected setEnv(text: string): void {
    this.envText.set(text);
    const { names, bad } = parseEnv(text);
    this.envBad.set(bad);
    if (bad.length === 0) this.patch({ authEnv: names });
  }

  protected setNotes(value: string): void { this.patch({ notes: value.trim() === '' ? null : value }); }
  protected setDirection(direction: FlowRule['direction']): void { this.patch({ direction }); }
  protected setVisibility(v: 'public' | 'private'): void { this.patch({ visibility: v }); }

  protected deleteSelected(): void {
    const cur = this.selected();
    if (!cur) return;
    this.rules.update((l) => l.filter((r) => r !== cur));
    this.hoverKey.set(null);
    this.select(null);
  }

  // ---- saving -----------------------------------------------------------------------------------
  protected async preview(): Promise<void> {
    if (this.envBad().length > 0 || this.phase() !== 'edit') return;
    this.error.set(null);
    this.phase.set('planning');
    try {
      this.plan.set(await this.store.planFlow(this.rules()));
      this.phase.set('plan');
    } catch (e) {
      this.error.set(e instanceof ApiError ? e.message : this.t('flow.planFailed'));
      this.phase.set('edit');
    }
  }

  protected backToEdit(): void { if (this.phase() === 'plan') { this.plan.set(null); this.phase.set('edit'); setTimeout(() => this.fit()); } }

  protected readonly actionable = computed(() => {
    const p = this.plan();
    return !!p && !p.blocked && p.repos.length > 0;
  });

  protected async confirm(): Promise<void> {
    if (this.phase() !== 'plan' || !this.actionable()) return;
    this.error.set(null);
    this.phase.set('saving');
    try {
      this.plan.set(await this.store.applyFlow(this.rules()));
      this.saved.set(this.rules());
      this.phase.set('done');
    } catch (e) {
      this.error.set(e instanceof ApiError ? e.message : this.t('flow.saveFailed'));
      this.phase.set('plan');
    }
  }
}
