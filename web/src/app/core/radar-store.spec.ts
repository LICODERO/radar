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
    pickFolder: vi.fn(async () => '/picked/dir')
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
