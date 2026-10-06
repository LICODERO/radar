import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { I18n } from '../i18n/i18n';
import { GapItem, Tool } from './commands';
import { AgentDraft, CopyAgentResult, EnableProjectResult, FileContent, ScanResult, Settings, SkillStatus, ToolStatus, ToolsInfo, VaultStatus, VisibilityOutcome } from './models';

export class ApiError extends Error {
  constructor(message: string, readonly status: number, readonly body?: any) {
    super(message);
  }
}

/** Thin client for the local R.A.D.A.R. server. Every call carries the per-run session token. */
@Injectable({ providedIn: 'root' })
export class RadarApi {
  private readonly http = inject(HttpClient);
  private readonly i18n = inject(I18n);
  private session: Promise<{ token: string; version?: string }> | null = null;

  private ensureSession(): Promise<{ token: string; version?: string }> {
    this.session ??= firstValueFrom(this.http.get<{ token: string; version?: string }>('/api/session'));
    return this.session;
  }

  private async ensureToken(): Promise<string> {
    return (await this.ensureSession()).token;
  }

  /** The app version reported by the server (null when it does not say). */
  async version(): Promise<string | null> {
    return (await this.ensureSession()).version ?? null;
  }

  /** Token plus the UI language, so the server answers with messages in the language the user sees. */
  private headers(token: string): Record<string, string> {
    return { 'X-Radar-Token': token, 'Accept-Language': this.i18n.lang() };
  }

  private async call<T>(method: string, url: string, body?: unknown): Promise<T> {
    const token = await this.ensureToken();
    try {
      return await firstValueFrom(this.http.request<T>(method, url, { body, headers: this.headers(token) }));
    } catch (e) {
      if (e instanceof HttpErrorResponse) {
        throw new ApiError(e.error?.error ?? this.i18n.t('api.serverError', { status: e.status }), e.status, e.error);
      }
      throw e;
    }
  }

  settings(): Promise<Settings> { return this.call('GET', '/api/settings'); }
  saveScanPath(scanPath: string): Promise<Settings> { return this.call('PUT', '/api/settings', { scanPath }); }

  /** null when the user cancelled the dialog */
  async pickFolder(): Promise<string | null> {
    const r = await this.call<{ path: string } | null>('POST', '/api/pick-folder');
    return r?.path ?? null;
  }

  async latest(): Promise<ScanResult | null> {
    try { return await this.call<ScanResult>('GET', '/api/scan/latest'); }
    catch (e) { if (e instanceof ApiError && e.status === 404) return null; throw e; }
  }

  readFile(repo: string, path: string): Promise<FileContent> {
    return this.call('GET', `/api/file?repo=${encodeURIComponent(repo)}&path=${encodeURIComponent(path)}`);
  }

  gaps(): Promise<GapItem[]> { return this.call('GET', '/api/gaps'); }
  toolsInfo(): Promise<ToolsInfo> { return this.call('GET', '/api/tools'); }
  toolStatuses(refresh = false): Promise<ToolStatus[]> { return this.call('GET', `/api/tools/status${refresh ? '?refresh=true' : ''}`); }

  /** Opens a terminal in the repo with claude/codex; the server builds the command itself. */
  run(repoId: string, type: string, tool: Tool): Promise<{ launched: boolean; toolFound: boolean }> {
    return this.call('POST', '/api/run', { repoId, type, tool });
  }

  /** Drafts an agent file from a description. Nothing is written; aborting the signal stops the claude call. */
  async generateAgent(repoId: string, description: string, signal: AbortSignal): Promise<AgentDraft> {
    const token = await this.ensureToken();
    return this.abortable(this.http.post<AgentDraft>('/api/agents/generate', { repoId, description }, { headers: this.headers(token) }), signal);
  }

  /** Writes the reviewed file as a brand-new .claude/agents/<name>.md (the server never overwrites). */
  /** `visibility: 'private'` also puts the new file on the repo's private list (.git/info/exclude); the server refuses when it cannot. */
  createAgent(repoId: string, content: string, visibility: 'private' | 'public' = 'public'): Promise<{ path: string; hidden?: boolean }> {
    return this.call('POST', '/api/agents', { repoId, content, visibility });
  }

  /** Without `confirm` the server only reports where the copy would land; with it, it adds the file to each repo (never overwriting). */
  copyAgent(fromRepoId: string, name: string, toRepoIds: string[], confirm: boolean, visibility: 'private' | 'public' = 'public'): Promise<CopyAgentResult> {
    return this.call('POST', '/api/agents/copy', { fromRepoId, name, toRepoIds, confirm, visibility });
  }

  /** Without `confirm` the server only plans the change; with it, it edits .git/info/exclude (and takes a tracked file out of the index). */
  setVisibility(repoId: string, path: string, visibility: 'private' | 'public', confirm: boolean): Promise<VisibilityOutcome> {
    return this.call('POST', '/api/visibility', { repoId, path, visibility, confirm });
  }

  // ---- second brain ----------------------------------------------------------------------------
  vault(): Promise<VaultStatus> { return this.call('GET', '/api/vault'); }
  /** New vault in an empty or new folder. Failures carry `body.code` (not-empty, already-vault, inside-repo...). */
  createVault(path: string): Promise<VaultStatus> { return this.call('POST', '/api/vault', { path }); }
  /** Points the app at an existing (for instance moved) vault. */
  relinkVault(path: string): Promise<VaultStatus> { return this.call('PUT', '/api/vault', { path }); }
  skill(): Promise<SkillStatus> { return this.call('GET', '/api/vault/skill'); }
  /** Installs the bundled skill; the server never overwrites, and updates only an untouched older copy when `update` is set. */
  async installSkill(update: boolean): Promise<SkillStatus> {
    return (await this.call<{ skill: SkillStatus }>('POST', '/api/vault/skill', { update })).skill;
  }
  /** Without `confirm` the server only returns what it would write. */
  enableProject(repoId: string, confirm: boolean): Promise<EnableProjectResult> {
    return this.call('POST', '/api/vault/projects', { repoId, confirm });
  }

  private abortable<T>(source: Observable<T>, signal: AbortSignal): Promise<T> {
    return new Promise<T>((resolve, reject) => {
      const sub = source.subscribe({
        next: resolve,
        error: (e) => reject(e instanceof HttpErrorResponse ? new ApiError(e.error?.error ?? this.i18n.t('api.serverError', { status: e.status }), e.status, e.error) : e)
      });
      signal.addEventListener('abort', () => { sub.unsubscribe(); reject(new DOMException(this.i18n.t('api.aborted'), 'AbortError')); }, { once: true });
    });
  }

  /** Starts a scan for one AI tool (the server only supports claude so far). */
  async startScan(tool = 'claude'): Promise<string> {
    try { return (await this.call<{ id: string }>('POST', '/api/scans', { tool })).id; }
    catch (e) {
      // a scan is already running: attach to it
      if (e instanceof ApiError && e.status === 409 && e.body?.id) return e.body.id;
      throw e;
    }
  }

  async currentScan(): Promise<string | null> {
    const r = await this.call<{ id: string } | null>('GET', '/api/scans/current');
    return r?.id ?? null;
  }

  cancelScan(id: string): Promise<void> { return this.call('DELETE', `/api/scans/${id}`); }

  /** Opens the SSE stream. EventSource cannot send headers, so the token goes in the query string. */
  async openEvents(id: string): Promise<EventSource> {
    const token = await this.ensureToken();
    return new EventSource(`/api/scans/${id}/events?token=${encodeURIComponent(token)}`);
  }
}
