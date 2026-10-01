import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { ApiError, RadarApi } from './radar-api';
import { RadarStore } from './radar-store';
import { ScanResult, Settings } from './models';

const settings = (path: string): Settings => ({ scanPath: path, scanPathDisplay: path, exists: true, maxDepth: 4, canPickFolder: true });

function setup(save: (p: string) => Promise<Settings>) {
  const api = {
    saveScanPath: vi.fn(save),
    startScan: vi.fn(async () => 'scan-1'),
    openEvents: vi.fn(async () => ({ addEventListener: vi.fn(), close: vi.fn(), readyState: 0 }) as unknown as EventSource),
    pickFolder: vi.fn(async () => '/picked/dir'),
    run: vi.fn(async () => ({ launched: true, toolFound: true })),
    gaps: vi.fn(async () => [{ repoId: 'r', repoName: 'r', initials: 'R', type: 'no-claude-md', dir: '/p/r', prompt: 'p' }]),
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

describe('gap commands', () => {
  it('loads the commands from the server when the generator opens', async () => {
    const { api, store } = setup(async (p) => settings(p));
    store.result.set({ repos: [{ id: 'r' }] } as never);
    await store.openGaps();
    expect(api.gaps).toHaveBeenCalledTimes(1);
    expect(store.gapsOpen()).toBe(true);
    expect(store.gapItems()).toHaveLength(1);
    expect(store.gapTotals()['no-claude-md']).toBe(1);
  });

  it('does not call the server for sample data', async () => {
    const { api, store } = setup(async (p) => settings(p));
    store.mode.set('mock');
    await store.openGaps();
    expect(api.gaps).not.toHaveBeenCalled();
    expect(store.gapItems()).toEqual([]);
  });

  describe('running a gap', () => {
    const item = { repoId: 'r', repoName: 'r', initials: 'R', type: 'no-agents', dir: '/p/r', prompt: 'p' } as const;
    const tools = { platform: 'macos', shell: 'posix', canLaunch: true, tools: { claude: true, codex: true }, toolPaths: { claude: '/n/claude', codex: null } } as const;

    it('asks first: the confirmation shows the command, by the full path when the server found the tool', () => {
      const { api, store } = setup(async (p) => settings(p));
      store.tools.set({ ...tools, tools: { ...tools.tools }, toolPaths: { ...tools.toolPaths } });
      store.askRun(item, 'claude');
      expect(api.run).not.toHaveBeenCalled();
      expect(store.pendingRun()).toMatchObject({ tool: 'claude', toolFound: true, command: "cd '/p/r' && '/n/claude' 'p'" });
      store.askRun(item, 'codex');
      expect(store.pendingRun()!.command).toBe("cd '/p/r' && codex 'p'"); // no path known: the plain name
    });

    it('runs by repo id, gap type and tool only, and clears the confirmation', async () => {
      const { api, store } = setup(async (p) => settings(p));
      store.askRun(item, 'codex');
      await store.confirmRun();
      expect(api.run).toHaveBeenCalledWith('r', 'no-agents', 'codex');
      expect(store.pendingRun()).toBeNull();
      expect(store.launchedKey()).toBe('r|no-agents');
      expect(store.running()).toBe(false);
    });

    it('keeps the confirmation open and shows the server message when the run is refused', async () => {
      const { api, store } = setup(async (p) => settings(p));
      api.run.mockRejectedValueOnce(new ApiError('Nie udało się otworzyć terminala: x', 500));
      store.askRun(item, 'claude');
      await store.confirmRun();
      expect(store.pendingRun()).not.toBeNull();
      expect(store.runError()).toBe('Nie udało się otworzyć terminala: x');
      expect(store.launchedKey()).toBeNull();
    });

    it('cancelling drops the confirmation without running anything', () => {
      const { api, store } = setup(async (p) => settings(p));
      store.askRun(item, 'claude');
      store.cancelRun();
      expect(store.pendingRun()).toBeNull();
      expect(api.run).not.toHaveBeenCalled();
    });

    it('names the terminal that opens', () => {
      const { store } = setup(async (p) => settings(p));
      store.tools.set({ ...tools, tools: { ...tools.tools }, terminal: 'iTerm2' });
      expect(store.terminalName()).toBe('iTerm2');
      store.tools.set({ ...tools, tools: { ...tools.tools } });
      expect(store.terminalName()).toBe('Terminal.app');
    });
  });

  it('closing the generator also drops a pending confirmation', () => {
    const { store } = setup(async (p) => settings(p));
    store.gapsOpen.set(true);
    store.pendingRun.set({ item: {} as never, tool: 'claude', command: 'x', toolFound: true });
    store.closeGaps();
    expect(store.pendingRun()).toBeNull();
    expect(store.gapsOpen()).toBe(false);
  });
});

describe('repo selection', () => {
  const repo = (id: string) => ({ id, name: id, agents: [], skills: [], coverage: { score: 50 }, gaps: [] });
  const load = (store: RadarStore) =>
    store.result.set({ repos: [repo('a'), repo('b')], workflows: [], gaps: [] } as unknown as ScanResult);

  it('selects no repo by default', () => {
    const { store } = setup(async (p) => settings(p));
    load(store);
    expect(store.selected()).toBeNull();
    expect(store.selectedWorkflows()).toEqual([]);
  });

  it('selects a repo on click and clears it on a second click', () => {
    const { store } = setup(async (p) => settings(p));
    load(store);
    store.toggleRepo('b');
    expect(store.selected()?.id).toBe('b');
    store.toggleRepo('b');
    expect(store.selected()).toBeNull();
  });

  it('goes back to no selection after a new scan, but keeps it on a quiet refresh', () => {
    const { store } = setup(async (p) => settings(p));
    load(store);
    const apply = (keep: boolean) => (store as unknown as { applyResult(r: ScanResult, keep: boolean): void })
      .applyResult(store.result()!, keep);
    store.toggleRepo('b');
    apply(true);
    expect(store.selected()?.id).toBe('b');
    apply(false);
    expect(store.selected()).toBeNull();
  });

  it('drops the repo selection when a workflow is picked, unless asked to keep it', () => {
    const { store } = setup(async (p) => settings(p));
    load(store);
    store.toggleRepo('a');
    store.pickItem({ kind: 'w', name: 'W1' }, true);
    expect(store.selected()?.id).toBe('a');
    store.pickItem({ kind: 'w', name: 'W1' });
    expect(store.selected()).toBeNull();
    expect(store.pick()).toEqual({ kind: 'w', name: 'W1' });
  });

  it('selects the repo of a picked agent', () => {
    const { store } = setup(async (p) => settings(p));
    load(store);
    store.pickItem({ kind: 'a', repoId: 'b', name: 'x' });
    expect(store.selected()?.id).toBe('b');
  });

  it('cannot open the agent composer without a selected repo', () => {
    const { store } = setup(async (p) => settings(p));
    load(store);
    store.mode.set('api');
    store.openComposer();
    expect(store.composerRepoId()).toBeNull();
  });
});

describe('second brain', () => {
  const vault = (state: 'none' | 'ok' | 'missing', projects: { name: string; repoId?: string }[] = []) => ({
    state, path: '/v', pathDisplay: '/v', projects, suggestedPath: '/s', suggestedPathDisplay: '/s'
  });
  const repo = (id: string) => ({ id, name: id, agents: [], skills: [], coverage: { score: 50 }, gaps: [] });

  function setupVault(initial = vault('none')) {
    const api = {
      vault: vi.fn(async () => initial),
      createVault: vi.fn(async (_p: string) => vault('ok')),
      relinkVault: vi.fn(async (_p: string) => vault('ok')),
      enableProject: vi.fn(async (_id: string, confirm: boolean) => ({
        applied: confirm,
        plan: { vaultDir: '/v/a', creates: [], claudeLocalPath: '/r/a/CLAUDE.local.md', claudeLocalExists: false, block: 'b', alreadyEnabled: confirm },
        vault: vault('ok', [{ name: 'a', repoId: 'a' }])
      }))
    };
    TestBed.configureTestingModule({ providers: [provideHttpClient(), { provide: RadarApi, useValue: api }] });
    const store = TestBed.inject(RadarStore);
    store.result.set({ repos: [repo('a'), repo('b')], workflows: [], gaps: [] } as unknown as ScanResult);
    return { api, store };
  }

  it('reads the status when the dialog opens and keeps working without it', async () => {
    const { api, store } = setupVault(vault('missing'));
    store.openVault();
    await vi.waitFor(() => expect(store.vault()?.state).toBe('missing'));
    expect(store.vaultOpen()).toBe(true);
    expect(store.vaultReady()).toBe(false);

    api.vault.mockRejectedValueOnce(new ApiError('x', 500));
    await store.loadVault();
    expect(store.vault()).toBeNull();
  });

  it('does not touch the server in sample-data mode', async () => {
    const { api, store } = setupVault();
    store.mode.set('mock');
    store.openVault();
    expect(api.vault).not.toHaveBeenCalled();
    expect(store.vaultOpen()).toBe(false);
  });

  it('remembers the status after create and relink', async () => {
    const { api, store } = setupVault();
    await store.createVault('/new');
    expect(api.createVault).toHaveBeenCalledWith('/new');
    expect(store.vaultReady()).toBe(true);

    store.vault.set(vault('missing') as never);
    await store.relinkVault('/moved');
    expect(api.relinkVault).toHaveBeenCalledWith('/moved');
    expect(store.vaultReady()).toBe(true);
  });

  it('passes the server error code through when creating fails', async () => {
    const { api, store } = setupVault();
    api.createVault.mockRejectedValueOnce(new ApiError('Folder nie jest pusty.', 409, { code: 'not-empty' }));
    await expect(store.createVault('/x')).rejects.toMatchObject({ status: 409, body: { code: 'not-empty' } });
    expect(store.vault()).toBeNull();
  });

  it('opens the repo dialog only for a selected repo and a working vault', async () => {
    const { store } = setupVault();
    store.selectRepo('a');
    store.openProject();
    expect(store.projectRepoId()).toBeNull(); // no vault yet

    store.vault.set(vault('ok') as never);
    store.clearSelection();
    store.openProject();
    expect(store.projectRepoId()).toBeNull(); // no repo selected

    store.selectRepo('a');
    store.openProject();
    expect(store.projectRepoId()).toBe('a');
    store.closeProject();
    expect(store.projectRepoId()).toBeNull();
  });

  it('previews without confirm, then confirms and learns that the repo is enabled', async () => {
    const { api, store } = setupVault();
    store.vault.set(vault('ok') as never);
    store.selectRepo('a');
    expect(store.selectedHasVault()).toBe(false);

    const preview = await store.planProject('a');
    expect(api.enableProject).toHaveBeenLastCalledWith('a', false);
    expect(preview.applied).toBe(false);
    expect(store.selectedHasVault()).toBe(false);

    await store.applyProject('a');
    expect(api.enableProject).toHaveBeenLastCalledWith('a', true);
    expect(store.selectedHasVault()).toBe(true);
    store.selectRepo('b');
    expect(store.selectedHasVault()).toBe(false);
  });
});
