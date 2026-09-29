import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { FileContent, ScanResult, Settings } from './models';

export class ApiError extends Error {
  constructor(message: string, readonly status: number, readonly body?: any) {
    super(message);
  }
}

/** Thin client for the local R.A.D.A.R server. Every call carries the per-run session token. */
@Injectable({ providedIn: 'root' })
export class RadarApi {
  private readonly http = inject(HttpClient);
  private token: Promise<string> | null = null;

  private ensureToken(): Promise<string> {
    this.token ??= firstValueFrom(this.http.get<{ token: string }>('/api/session')).then((r) => r.token);
    return this.token;
  }

  private async call<T>(method: string, url: string, body?: unknown): Promise<T> {
    const token = await this.ensureToken();
    try {
      return await firstValueFrom(this.http.request<T>(method, url, { body, headers: { 'X-Radar-Token': token } }));
    } catch (e) {
      if (e instanceof HttpErrorResponse) {
        throw new ApiError(e.error?.error ?? `Błąd serwera (${e.status})`, e.status, e.error);
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

  async startScan(): Promise<string> {
    try { return (await this.call<{ id: string }>('POST', '/api/scans')).id; }
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
