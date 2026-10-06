import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { parseAgent, setAgentName, validateAgent } from '../core/agent-file';
import { ItemKind } from '../core/models';
import { MsgKey } from '../i18n/pl';
import { ApiError, RadarApi } from '../core/radar-api';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { MdView } from '../shared/md-view';
import { NewVisibility, VisibilityPicker } from '../shared/visibility-picker';

type Phase = 'input' | 'generating' | 'draft' | 'saving' | 'saved';

const MAX_DESCRIPTION = 2000;

@Component({
  selector: 'app-agent-composer',
  imports: [MdView, VisibilityPicker],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './agent-composer.html',
  styleUrl: './agent-composer.scss'
})
export class AgentComposer {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  private readonly api = inject(RadarApi);
  private abort: AbortController | null = null;

  protected readonly max = MAX_DESCRIPTION;
  protected readonly phase = signal<Phase>('input');
  protected readonly description = signal('');
  protected readonly content = signal('');
  protected readonly mode = signal<'preview' | 'edit'>('preview');
  protected readonly error = signal<string | null>(null);
  protected readonly serverErrors = signal<string[]>([]);
  protected readonly cost = signal<number | null>(null);
  protected readonly savedPath = signal<string | null>(null);
  protected readonly visibility = signal<NewVisibility>('private');
  protected readonly savedHidden = signal(false);
  protected readonly rescanning = signal(false);
  protected readonly kind = computed<ItemKind>(() => this.store.composerKind());
  /** every text that differs between an agent, a skill and a workflow comes from `agent.*` or `composer.<kind>.*` */
  protected readonly x = (suffix: string, params?: Record<string, string | number>): string =>
    this.t((this.kind() === 'agent' ? `agent.${suffix}` : `composer.${this.kind()}.${suffix}`) as MsgKey, params);

  protected readonly repo = computed(() => this.store.repos().find((r) => r.id === this.store.composerRepoId()) ?? null);
  protected readonly name = computed(() => parseAgent(this.content()).name);
  protected readonly targetPath = computed(() => {
    const name = this.name() || this.t('agent.namePlaceholder');
    return this.kind() === 'skill' ? `.claude/skills/${name}/SKILL.md` : this.kind() === 'workflow' ? `.claude/workflows/${name}.md` : `.claude/agents/${name}.md`;
  });
  protected readonly clientErrors = computed(() => validateAgent(this.content(), this.t));
  protected readonly problems = computed(() => [...this.clientErrors(), ...this.serverErrors().filter((e) => !this.clientErrors().includes(e))]);
  protected readonly claudeMissing = computed(() => this.store.tools()?.tools['claude'] === false);
  protected readonly canGenerate = computed(() => this.description().trim().length > 0 && this.description().length <= MAX_DESCRIPTION);
  protected readonly canSave = computed(() => this.phase() === 'draft' && this.clientErrors().length === 0);
  protected readonly busy = computed(() => this.phase() === 'generating' || this.phase() === 'saving');
  /** names already in the repo that are sent along with the description (a workflow may refer to agents and skills) */
  protected readonly existing = computed(() => {
    const r = this.repo();
    if (!r) return [];
    const agents = r.agents.map((a) => a.name);
    const skills = r.skills.map((s) => s.name);
    return this.kind() === 'skill' ? skills : this.kind() === 'workflow' ? [...agents, ...skills] : agents;
  });

  private readonly firstField = viewChild<ElementRef<HTMLTextAreaElement>>('desc');

  constructor() {
    effect(() => {
      const el = this.firstField();
      if (el) queueMicrotask(() => el.nativeElement.focus({ preventScroll: true }));
    });
  }

  protected onDescription(e: Event): void { this.description.set((e.target as HTMLTextAreaElement).value); }
  protected onContent(e: Event): void { this.content.set((e.target as HTMLTextAreaElement).value); this.serverErrors.set([]); }
  protected onName(e: Event): void { this.content.set(setAgentName(this.content(), (e.target as HTMLInputElement).value.trim())); this.serverErrors.set([]); }

  protected async generate(): Promise<void> {
    const repo = this.repo();
    if (!repo || !this.canGenerate() || this.busy()) return;
    this.abort = new AbortController();
    this.error.set(null);
    this.serverErrors.set([]);
    this.phase.set('generating');
    try {
      const kind = this.kind();
      const draft = kind === 'agent'
        ? await this.api.generateAgent(repo.id, this.description().trim(), this.abort.signal)
        : await this.api.generateItem(kind, repo.id, this.description().trim(), this.abort.signal);
      this.content.set(draft.content);
      this.serverErrors.set(draft.errors);
      this.cost.set(draft.costUsd ?? null);
      this.mode.set('preview');
      this.phase.set('draft');
    } catch (e) {
      this.phase.set(this.content() ? 'draft' : 'input');
      if (!(e instanceof DOMException && e.name === 'AbortError')) {
        this.error.set(e instanceof ApiError ? e.message : this.x('generateFailed'));
      }
    } finally {
      this.abort = null;
    }
  }

  protected stop(): void { this.abort?.abort(); }

  protected async save(): Promise<void> {
    const repo = this.repo();
    if (!repo || !this.canSave()) return;
    this.error.set(null);
    this.phase.set('saving');
    try {
      const kind = this.kind();
      const r = kind === 'agent'
        ? await this.api.createAgent(repo.id, this.content(), this.visibility())
        : await this.api.createItem(kind, repo.id, this.content(), this.visibility());
      this.savedPath.set(r.path);
      this.savedHidden.set(!!r.hidden);
      this.phase.set('saved');
      this.rescanning.set(true);
      await this.store.refreshQuiet();
      this.rescanning.set(false);
    } catch (e) {
      this.rescanning.set(false);
      if (this.phase() === 'saving') this.phase.set('draft');
      this.error.set(e instanceof ApiError ? e.message : this.t('agent.saveFailed'));
    }
  }

  protected openSaved(): void {
    const repo = this.repo();
    const path = this.savedPath();
    if (!repo || !path) return;
    this.store.closeComposer();
    const kind = this.kind();
    void this.store.openFile(repo.id, path, kind === 'skill' ? 'SKILL' : kind === 'workflow' ? 'WORKFLOW' : 'AGENT', kind === 'skill' ? '#a99bff' : kind === 'workflow' ? '#4dd6ff' : '#c6ff3d');
  }

  protected another(): void {
    this.content.set('');
    this.description.set('');
    this.savedPath.set(null);
    this.serverErrors.set([]);
    this.cost.set(null);
    this.phase.set('input');
  }

  protected close(): void {
    this.abort?.abort();
    this.store.closeComposer();
  }
}
