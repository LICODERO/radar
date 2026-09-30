import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { parseAgent, setAgentName, validateAgent } from '../core/agent-file';
import { ApiError, RadarApi } from '../core/radar-api';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { MdView } from '../shared/md-view';

type Phase = 'input' | 'generating' | 'draft' | 'saving' | 'saved';

const MAX_DESCRIPTION = 2000;

@Component({
  selector: 'app-agent-composer',
  imports: [MdView],
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
  protected readonly rescanning = signal(false);

  protected readonly repo = computed(() => this.store.repos().find((r) => r.id === this.store.composerRepoId()) ?? null);
  protected readonly name = computed(() => parseAgent(this.content()).name);
  protected readonly targetPath = computed(() => `.claude/agents/${this.name() || this.t('agent.namePlaceholder')}.md`);
  protected readonly clientErrors = computed(() => validateAgent(this.content(), this.t));
  protected readonly problems = computed(() => [...this.clientErrors(), ...this.serverErrors().filter((e) => !this.clientErrors().includes(e))]);
  protected readonly claudeMissing = computed(() => this.store.tools()?.tools['claude'] === false);
  protected readonly canGenerate = computed(() => this.description().trim().length > 0 && this.description().length <= MAX_DESCRIPTION);
  protected readonly canSave = computed(() => this.phase() === 'draft' && this.clientErrors().length === 0);
  protected readonly busy = computed(() => this.phase() === 'generating' || this.phase() === 'saving');
  protected readonly existing = computed(() => this.repo()?.agents.map((a) => a.name) ?? []);

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
      const draft = await this.api.generateAgent(repo.id, this.description().trim(), this.abort.signal);
      this.content.set(draft.content);
      this.serverErrors.set(draft.errors);
      this.cost.set(draft.costUsd ?? null);
      this.mode.set('preview');
      this.phase.set('draft');
    } catch (e) {
      this.phase.set(this.content() ? 'draft' : 'input');
      if (!(e instanceof DOMException && e.name === 'AbortError')) {
        this.error.set(e instanceof ApiError ? e.message : this.t('agent.generateFailed'));
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
      const r = await this.api.createAgent(repo.id, this.content());
      this.savedPath.set(r.path);
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
    void this.store.openFile(repo.id, path, 'AGENT', '#c6ff3d');
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
