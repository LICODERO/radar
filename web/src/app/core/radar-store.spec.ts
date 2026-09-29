import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { ApiError, RadarApi } from './radar-api';
import { RadarStore } from './radar-store';
import { Settings } from './models';

const settings = (path: string): Settings => ({ scanPath: path, scanPathDisplay: path, exists: true, maxDepth: 4, canPickFolder: true });

function setup(save: (p: string) => Promise<Settings>) {
  const api = {
    saveScanPath: vi.fn(save),
    startScan: vi.fn(async () => 'scan-1'),
    openEvents: vi.fn(async () => ({ addEventListener: vi.fn(), close: vi.fn(), readyState: 0 }) as unknown as EventSource),
    pickFolder: vi.fn(async () => '/picked/dir'),
    readFile: vi.fn(async (_r: string, path: string) => ({ path, content: '# ' + path, truncated: false, bytes: 10 }))
  };
  TestBed.configureTestingModule({ providers: [provideHttpClient(), { provide: RadarApi, useValue: api }] });
  return { api, store: TestBed.inject(RadarStore) };
}

describe('changing the scan location', () => {
  it('starts a scan right after the new path is accepted', async () => {
    const { api, store } = setup(async (p) => settings(p));
    const ok = await store.setScanPath('/some/dir');
    expect(ok).toBe(true);
    expect(api.saveScanPath).toHaveBeenCalledWith('/some/dir');
    expect(api.startScan).toHaveBeenCalledTimes(1);
    expect(store.scan()?.status).toBe('running');
  });

  it('does not scan when the server rejects the path', async () => {
    const { api, store } = setup(async () => { throw new ApiError('Katalog nie istnieje.', 400); });
    const ok = await store.setScanPath('/nope');
    expect(ok).toBe(false);
    expect(api.startScan).not.toHaveBeenCalled();
    expect(store.scan()).toBeNull();
    expect(store.notice()).toBe('Katalog nie istnieje.');
  });

  it('scans after a folder is picked in the native dialog', async () => {
    const { api, store } = setup(async (p) => settings(p));
    await store.pickFolder();
    expect(api.saveScanPath).toHaveBeenCalledWith('/picked/dir');
    expect(api.startScan).toHaveBeenCalledTimes(1);
  });

  it('does nothing when the dialog is cancelled', async () => {
    const { api, store } = setup(async (p) => settings(p));
    api.pickFolder.mockResolvedValueOnce(null as unknown as string);
    await store.pickFolder();
    expect(api.saveScanPath).not.toHaveBeenCalled();
    expect(api.startScan).not.toHaveBeenCalled();
  });
});

describe('file preview', () => {
  it('loads a file read-only and exposes it to the pane', async () => {
    const { api, store } = setup(async (p) => settings(p));
    await store.openFile('orders-api', 'CLAUDE.md', 'CLAUDE.md', '#c6ff3d');
    expect(api.readFile).toHaveBeenCalledWith('orders-api', 'CLAUDE.md');
    const f = store.file()!;
    expect(f.status).toBe('ready');
    expect(f.text).toBe('# CLAUDE.md');
    expect(f.mode).toBe('preview');
    store.setFileMode('source');
    expect(store.file()!.mode).toBe('source');
    store.closeFile();
    expect(store.file()).toBeNull();
  });

  it('shows the server message when the file cannot be read', async () => {
    const { api, store } = setup(async (p) => settings(p));
    api.readFile.mockRejectedValueOnce(new ApiError('Odczyt tego pliku jest zabroniony.', 403));
    await store.openFile('r', 'x.md', 'AGENT', '#c6ff3d');
    expect(store.file()!.status).toBe('error');
    expect(store.file()!.error).toBe('Odczyt tego pliku jest zabroniony.');
  });

  it('ignores a slow response for a file that was replaced or closed', async () => {
    const { api, store } = setup(async (p) => settings(p));
    let release!: (v: { path: string; content: string; truncated: boolean; bytes: number }) => void;
    api.readFile.mockImplementationOnce(() => new Promise((res) => { release = res; }));
    const slow = store.openFile('r', 'slow.md', 'AGENT', '#c6ff3d');
    store.closeFile();
    release({ path: 'slow.md', content: 'late', truncated: false, bytes: 4 });
    await slow;
    expect(store.file()).toBeNull();
  });

  it('does not call the server for sample data', async () => {
    const { api, store } = setup(async (p) => settings(p));
    store.mode.set('mock');
    await store.openFile('r', 'CLAUDE.md', 'CLAUDE.md', '#c6ff3d');
    expect(api.readFile).not.toHaveBeenCalled();
    expect(store.file()!.status).toBe('error');
  });
});
