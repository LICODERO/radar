import { atom, read, update } from 'claude-code'
import type { Register } from 'claude-code'

import { bandOf, binaryOf, compareVersions, COMMANDS, describe, homeOf, installPlan, isRadarHealth, joinPath, osOf, parentDir, parseLatestTag, parseProps, parseSessionVersion, parseVersionFile, pickSource, powershell, runScriptOf, BASE_URL, PORT, RAW, USAGE } from './radar'
import type { Os } from './radar'
import type { Facts, Probe } from '../types'

/** `$` as the state helpers take it, plus the calls this mod makes. */
type Api = Parameters<typeof update>[0] & {
  env: { get(name: string): Promise<string | undefined> }
  fs: { read(path: string): Promise<string>; exists(path: string): Promise<boolean> }
  process: {
    run(
      argv: readonly string[],
      init?: { cwd?: string; env?: Record<string, string>; timeoutMs?: number },
    ): Promise<{ exitCode: number; stdout: string; stderr: string }>
  }
  session: { cwd(): Promise<string> }
  command: { register(spec: { name: string; description: string }): Promise<unknown> }
  clock: { sleep(ms: number): Promise<void>; every(ms: number, fn: () => void): unknown }
  ui: { toast(text: string): void; open(pane: { id: string; title?: string; focus?: boolean; closeOnEscape?: boolean }): Promise<unknown> }
  store: { get(key: string): Promise<unknown>; set(key: string, value: unknown): Promise<void> }
}

const COMMAND = 'radar'
const REFRESH_MS = 15_000

const factsAtom = atom({ plugin: 'radar', key: 'facts' } as const, null)
const busyAtom = atom({ plugin: 'radar', key: 'busy' } as const, null)
const hiddenAtom = atom({ plugin: 'radar', key: 'isHidden' } as const, false)
const outputAtom = atom({ plugin: 'radar', key: 'output' } as const, null)
const pendingAtom = atom({ plugin: 'radar', key: 'pending' } as const, null)

const PANE = 'radar-commands'

// the module's state; a reload starts it over and ensureReady fills it again
let cwd = ''
let isSupported = true
let os: Os = 'unix'
let ready: Promise<void> | null = null

async function radarHome($: Api): Promise<string> {
  return homeOf(os, {
    custom: await $.env.get('RADAR_HOME'),
    home: await $.env.get('HOME'),
    userProfile: await $.env.get('USERPROFILE'),
    localAppData: await $.env.get('LOCALAPPDATA'),
  })
}

async function readText($: Api, path: string): Promise<string | null> {
  try {
    return (await $.fs.exists(path)) ? await $.fs.read(path) : null
  } catch {
    return null
  }
}

/** The checkout the session works in: the session's folder or the nearest one above it (a session often sits in a subfolder). */
async function findRepo($: Api, start: string): Promise<string | null> {
  const launcher = os === 'windows' ? 'run.ps1' : 'run.sh'
  let dir = start
  for (let i = 0; i < 8 && dir.length > 1; i++) {
    if (
      (await $.fs.exists(joinPath(os, dir, 'Directory.Build.props'))) &&
      (await $.fs.exists(joinPath(os, dir, 'src', 'api', 'Radar.Server'))) &&
      (await $.fs.exists(joinPath(os, dir, launcher)))
    )
      return dir
    const up = parentDir(dir)
    if (up === '' || up === dir) break
    dir = up
  }
  return null
}

/** The user's command line tools: `curl.exe` is the one shipped with Windows 10+, plain `curl` elsewhere. */
const curl = () => (os === 'windows' ? 'curl.exe' : 'curl')

/** Runs a short script in the system's shell; its values come in through `env`, never through the text. */
function shell($: Api, unix: string, win: string, env: Record<string, string>, timeoutMs: number) {
  const argv = os === 'windows' ? powershell(win) : ['/bin/sh', '-c', unix]
  return $.process.run(argv, { env, timeoutMs })
}

async function probe($: Api): Promise<Probe> {
  try {
    const health = await $.process.run([curl(), '-s', '-m', '2', `${BASE_URL}/api/health`], { timeoutMs: 5000 })
    if (health.exitCode !== 0 || !isRadarHealth(health.stdout)) return { isRunning: false, version: null }
    const session = await $.process.run([curl(), '-s', '-m', '2', `${BASE_URL}/api/session`], { timeoutMs: 5000 })
    return { isRunning: true, version: parseSessionVersion(session.stdout) }
  } catch {
    return { isRunning: false, version: null }
  }
}

async function facts($: Api): Promise<Facts> {
  const home = await radarHome($)
  const binary = binaryOf(os, home)
  const [isInstalled, versionText, root] = await Promise.all([
    $.fs.exists(binary),
    readText($, joinPath(os, home, 'app', 'VERSION')),
    findRepo($, cwd),
  ])
  const propsText = root ? await readText($, joinPath(os, root, 'Directory.Build.props')) : null
  return {
    isSupported,
    probe: await probe($),
    installedVersion: parseVersionFile(versionText),
    isInstalled,
    dev: root ? { version: parseProps(propsText), root } : null,
  }
}

/** Probes again and hands the row above the prompt what it draws. */
async function refresh($: Api): Promise<Facts> {
  const f = await facts($)
  await update($, factsAtom, () => f)
  return f
}

async function waitFor($: Api, wantRunning: boolean, seconds: number): Promise<boolean> {
  for (let i = 0; i < seconds; i++) {
    if ((await probe($)).isRunning === wantRunning) return true
    await $.clock.sleep(1000)
  }
  return false
}

async function start($: Api, arg: string, f: Facts): Promise<string> {
  if (f.probe.isRunning) return `Już działa: ${BASE_URL}`
  const source = pickSource(arg, f)
  if (source === 'none')
    return arg === 'dev'
      ? 'To nie jest repo R.A.D.A.R. (brak run.sh).'
      : 'R.A.D.A.R. nie jest zainstalowany. Użyj /radar install.'
  const home = await radarHome($)
  const logs = joinPath(os, home, 'logs')
  const root = f.dev?.root ?? cwd
  // the program runs on after the command: detached, its output in a log, never into this call's pipes
  if (os === 'windows') {
    const launch = source === 'dev' ? runScriptOf(os, root) : { file: binaryOf(os, home), args: '' }
    const script =
      "New-Item -ItemType Directory -Force -Path $env:RL | Out-Null; " +
      "$a = @{ FilePath = $env:RC; WorkingDirectory = $env:RD; WindowStyle = 'Hidden'; " +
      "RedirectStandardOutput = (Join-Path $env:RL 'radar.log'); RedirectStandardError = (Join-Path $env:RL 'radar.err.log') }; " +
      'if ($env:RA) { $a.ArgumentList = $env:RA }; Start-Process @a'
    await $.process.run(powershell(script), {
      env: { RL: logs, RD: source === 'dev' ? root : home, RC: launch.file, RA: launch.args },
      timeoutMs: 15_000,
    })
  } else {
    const script = 'mkdir -p "$RL" && cd "$RD" && nohup "$RC" > "$RL/radar.log" 2>&1 < /dev/null &'
    const env =
      source === 'dev'
        ? { RL: logs, RD: root, RC: runScriptOf(os, root).file }
        : { RL: logs, RD: home, RC: binaryOf(os, home) }
    await $.process.run(['/bin/sh', '-c', script], { env, timeoutMs: 10_000 })
  }
  const where = source === 'dev' ? 'ze źródeł (run.sh / run.ps1, pierwszy start buduje UI)' : 'zainstalowaną kopię'
  if (await waitFor($, true, source === 'dev' ? 40 : 20)) return `Uruchomiono ${where}: ${BASE_URL}`
  return `Uruchamiam ${where}, ale jeszcze nie odpowiada. Sprawdź za chwilę: /radar status (log: ${joinPath(os, logs, 'radar.log')}).`
}

async function stop($: Api, f: Facts): Promise<string> {
  if (!f.probe.isRunning) return 'Nie działa.'
  const found =
    os === 'windows'
      ? await $.process.run(
          powershell(`Get-NetTCPConnection -LocalPort ${PORT} -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess`),
          { timeoutMs: 10_000 },
        )
      : await $.process.run(['lsof', '-nP', `-iTCP:${PORT}`, '-sTCP:LISTEN', '-t'], { timeoutMs: 5000 })
  const pids = [...new Set(found.stdout.split(/\s+/).filter(p => /^\d+$/.test(p)))]
  if (found.exitCode !== 0 || pids.length !== 1) return `Nie umiem ustalić procesu na porcie ${PORT}, nic nie zatrzymuję.`
  // it was recognised as R.A.D.A.R. by its health answer just now; a single listener on its port is that process
  if (os === 'windows') await $.process.run(powershell(`Stop-Process -Id ${pids[0]} -Force`), { timeoutMs: 10_000 })
  else await $.process.run(['kill', pids[0]], { timeoutMs: 5000 })
  return (await waitFor($, false, 10)) ? 'Zatrzymano.' : `Proces ${pids[0]} jeszcze działa; sprawdź /radar status.`
}

async function open($: Api, f: Facts): Promise<string> {
  if (!f.probe.isRunning) return 'Nie działa. Użyj /radar start.'
  if (os === 'windows') await $.process.run(powershell(`Start-Process '${BASE_URL}'`), { timeoutMs: 10_000 })
  else {
    const uname = await $.process.run(['uname', '-s'], { timeoutMs: 3000 })
    await $.process.run([uname.stdout.trim() === 'Darwin' ? 'open' : 'xdg-open', BASE_URL], { timeoutMs: 5000 })
  }
  return `Otwarto ${BASE_URL}`
}

/** The start/stop button: the row shows it busy while the program comes up or goes down, and a toast says how it ended. */
async function toggle($: Api): Promise<void> {
  const f = await facts($)
  await update($, busyAtom, () => (f.probe.isRunning ? 'stop' : 'start'))
  try {
    $.ui.toast(f.probe.isRunning ? await stop($, f) : await start($, '', f))
  } finally {
    await update($, busyAtom, () => null)
    await refresh($)
  }
}

async function latestRelease($: Api): Promise<string | null> {
  try {
    const r = await $.process.run(
      [curl(), '-s', '-m', '5', '-H', 'Accept: application/vnd.github+json', 'https://api.github.com/repos/LICODERO/radar/releases/latest'],
      { timeoutMs: 8000 },
    )
    return parseLatestTag(r.stdout)
  } catch {
    return null
  }
}

/** The installer of the repository, fetched to a temporary file and run (it checks the archive's SHA256 itself). */
async function runInstaller($: Api): Promise<string> {
  const r =
    os === 'windows'
      ? await shell(
          $,
          '',
          "$f = Join-Path $env:TEMP 'radar-install.ps1'; Invoke-WebRequest -UseBasicParsing -Uri $env:U -OutFile $f; " +
            "& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $f; $rc = $LASTEXITCODE; Remove-Item $f -ErrorAction SilentlyContinue; exit $rc",
          { U: `${RAW}/install.ps1` },
          300_000,
        )
      : await shell(
          $,
          'f=$(mktemp) && curl -fsSL "$U" -o "$f" && sh "$f"; rc=$?; rm -f "$f"; exit $rc',
          '',
          { U: `${RAW}/install.sh` },
          300_000,
        )
  const tail = `${r.stdout}\n${r.stderr}`.trim().split('\n').slice(-8).join('\n')
  return r.exitCode === 0 ? tail : `Instalator zakończył się błędem (kod ${r.exitCode}):\n${tail}`
}

async function install($: Api, f: Facts, verb: 'install' | 'update', isConfirmed: boolean): Promise<string> {
  if (verb === 'install' && f.isInstalled) return 'R.A.D.A.R. jest już zainstalowany. Do aktualizacji użyj /radar update.'
  if (verb === 'update' && !f.isInstalled) return 'R.A.D.A.R. nie jest zainstalowany. Użyj /radar install.'
  if (!isConfirmed) return installPlan(verb, f, await latestRelease($), os)
  const was = f.installedVersion
  let stopped = ''
  if (verb === 'update' && f.probe.isRunning) stopped = `${await stop($, f)}\n`
  const out = await runInstaller($)
  const after = parseVersionFile(await readText($, joinPath(os, await radarHome($), 'app', 'VERSION')))
  return `${stopped}${out}\nWersja: ${was ?? 'nieznana'} → ${after ?? 'nieznana'}. Uruchom: /radar start`
}

/** Runs the program's own `uninstall` (a listing until `confirm`); copies without that command are refused instead of started. */
async function uninstall($: Api, f: Facts, args: string): Promise<string> {
  if (!f.isInstalled) return 'Nie ma czego usuwać (brak zainstalowanej kopii).'
  if (!f.installedVersion || compareVersions(f.installedVersion, '0.1.2') < 0)
    return `Ta kopia nie ma komendy uninstall (albo nie umiem ustalić jej wersji). Usuń ręcznie: ${
      os === 'windows' ? 'Remove-Item -Recurse -Force "$env:LOCALAPPDATA\\RADAR"' : 'rm -rf ~/.radar ~/.local/bin/radar'
    }`
  const words = args.split(/\s+/).filter(w => w !== '')
  const isConfirmed = words.includes('confirm')
  const home = await radarHome($)
  let stopped = ''
  if (isConfirmed && f.probe.isRunning) stopped = `${await stop($, f)}\n`
  const argv = [binaryOf(os, home), 'uninstall']
  if (isConfirmed) argv.push('--confirm')
  if (words.includes('data')) argv.push('--data')
  if (words.includes('skill')) argv.push('--skill')
  const r = await $.process.run(argv, { timeoutMs: 30_000 })
  const out = `${r.stdout}${r.stderr}`.trim()
  const hint = isConfirmed ? '' : '\nPotwierdź: /radar uninstall confirm (dodaj `data`, by usunąć też ustawienia, i `skill`, by usunąć skill radar-second-brain)'
  return `${stopped}${out}${hint}`
}

/** One command, whoever asked: the slash command or a row of the commands pane. */
async function execute($: Api, verb: string, args: string): Promise<string> {
  const f = await facts($)
  const arg = args.trim().split(/\s+/)[0] ?? ''
  if (!f.isSupported && verb !== 'status') return describe(f)
  switch (verb) {
    case '':
    case 'status':
      return describe(f)
    case 'start':
      return start($, arg, f)
    case 'stop':
      return stop($, f)
    case 'restart':
      return f.probe.isRunning
        ? `${await stop($, f)}\n${await start($, arg, { ...f, probe: { isRunning: false, version: null } })}`
        : start($, arg, f)
    case 'open':
      return open($, f)
    case 'hide':
    case 'show':
    case 'toggle': {
      const isHidden = verb === 'hide' ? true : verb === 'show' ? false : !(await read($, hiddenAtom))
      await setHidden($, isHidden)
      return isHidden ? 'Panel ukryty. Przywrócisz go przez /radar show.' : 'Panel widoczny.'
    }
    case 'install':
    case 'update':
      return install($, f, verb, arg === 'confirm')
    case 'uninstall':
      return uninstall($, f, args)
    default:
      return USAGE
  }
}

/** A row of the commands pane: the answer shows in the pane, and a plan waits there for its confirm button. */
async function runFromPane($: Api, verb: string, args: string): Promise<void> {
  const isPlan = (verb === 'install' || verb === 'update' || verb === 'uninstall') && args !== 'confirm'
  await update($, outputAtom, () => '…')
  const text = await execute($, verb, args)
  await update($, outputAtom, () => text)
  await update($, pendingAtom, () => (isPlan ? (verb as 'install' | 'update' | 'uninstall') : null))
  await refresh($)
}

async function openPane($: Api): Promise<void> {
  await update($, outputAtom, () => null)
  await update($, pendingAtom, () => null)
  await $.ui.open({ id: PANE, title: 'R.A.D.A.R.', focus: true, closeOnEscape: true })
}

/** Shows or hides the row above the prompt; the choice is kept across sessions. */
async function setHidden($: Api, isHidden: boolean): Promise<void> {
  await update($, hiddenAtom, () => isHidden)
  await $.store.set('isHidden', isHidden)
  if (!isHidden) await refresh($)
}

/** The periodic probe: nothing to draw while the row is hidden, so nothing to ask. */
async function tick($: Api): Promise<void> {
  if (!(await read($, hiddenAtom))) await refresh($)
}

async function openBrowser($: Api): Promise<void> {
  $.ui.toast(await open($, await facts($)))
}

/**
 * Everything the row and the command need once: the system, the folder, the command, the timer, the first probe.
 * Started from `session.start` and, because a plugin enabled or reloaded in a running session may never see that event, from the first
 * render or command too; without that the row would stay empty until the next session.
 */
function ensureReady($: Api): Promise<void> {
  ready ??= (async () => {
    try {
      cwd = await $.session.cwd()
    } catch {
      cwd = ''
    }
    os = osOf(await $.env.get('OS'))
    if (os === 'unix') {
      try {
        await $.process.run(['uname', '-s'], { timeoutMs: 3000 })
      } catch {
        isSupported = false
      }
    }
    const isHiddenBefore = await $.store.get('isHidden')
    await $.command.register({
      name: COMMAND,
      description: 'R.A.D.A.R.: status, start, stop, restart, open, hide, show, install, update, uninstall',
    })
    await update($, hiddenAtom, () => isHiddenBefore === true)
    await refresh($)
    $.clock.every(REFRESH_MS, () => {
      void tick($)
    })
  })().catch(() => {
    ready = null
  })
  return ready
}

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    await ensureReady($)
    return next(e)
  })

  on('command.run', { command: COMMAND }, async ($, e) => {
    await ensureReady($)
    const [verb = 'status', ...rest] = e.args.trim().split(/\s+/)
    const text = await execute($, verb, rest.join(' '))
    await refresh($)
    return { text }
  }).catch(($, e, next) => (next.called ? next(e) : { text: 'R.A.D.A.R.: komenda się nie powiodła. Sprawdź /radar status.' }))

  // the row above the prompt: version, state and the buttons. The band has one slot, so this row goes under whatever the hooks beneath
  // draw (another plugin's band) instead of replacing it, and it never swallows a survey.
  on('ui.render', { component: 'AbovePrompt' }, async ($, e, next) => {
    const below = await next(e)
    const f = await read($, factsAtom)
    if (f === null) {
      // nothing probed yet (the plugin came in after session.start): start it, the next render draws the row
      void ensureReady($)
      return below
    }
    if (await read($, hiddenAtom)) return below
    const band = bandOf(f)
    if (!band.isVisible || e.props.hasSurvey) return below

    const busy = await read($, busyAtom)
    const { Box, Button, Text } = $.ui.resolve(e)
    const state = busy === 'start' ? 'uruchamiam…' : busy === 'stop' ? 'zatrzymuję…' : ''

    // the name and state at the left edge, the buttons at the right one
    const row = (
      <Box justifyContent="space-between" width="100%" paddingX={1}>
        <Box>
          <Text dimColor>R.A.D.A.R. </Text>
          {band.version !== null && <Button key="commands" label={`v${band.version}`} plain dimColor onPress={() => openPane($)} />}
          {band.version !== null && <Text>  </Text>}
          <Text color={band.isRunning ? 'success' : 'error'}>● </Text>
          {state !== '' && <Text dimColor>{state}</Text>}
        </Box>
        <Box>
          {busy === null && (band.isRunning || band.canStart) && (
            <Button key="toggle" label={band.isRunning ? '■' : '▶'} plain dimColor onPress={() => toggle($)} />
          )}
          {busy === null && band.isRunning && <Text>  </Text>}
          {busy === null && band.isRunning && <Button key="open" label="↗" plain dimColor onPress={() => openBrowser($)} />}
          {busy === null && <Text>  </Text>}
          {busy === null && <Button key="hide" label="✕" plain dimColor onPress={() => setHidden($, true)} />}
        </Box>
      </Box>
    )
    if (below === null || below === undefined || typeof below !== 'object') return row
    return (
      <Box flexDirection="column" width="100%">
        {below}
        {row}
      </Box>
    )
  })

  // the commands pane: every /radar command as a row, with the last answer under it
  on('ui.render', { component: 'Pane', requestId: PANE }, async ($, e) => {
    const { Box, Button, Text } = $.ui.resolve(e)
    const output = await read($, outputAtom)
    const pending = await read($, pendingAtom)

    return (
      <Box flexDirection="column" paddingX={1}>
        {COMMANDS.map(c => (
          <Box key={c.verb}>
            <Button key={`run-${c.verb}`} label={`/radar ${c.verb}`} plain onPress={() => runFromPane($, c.verb, '')} />
            <Text dimColor>  {c.hint}</Text>
          </Box>
        ))}
        {output !== null && (
          <Box flexDirection="column" marginTop={1}>
            <Text>{output}</Text>
            {pending !== null && (
              <Button key="confirm" label={`Potwierdź: /radar ${pending} confirm`} variant="primary" onPress={() => runFromPane($, pending, 'confirm')} />
            )}
          </Box>
        )}
      </Box>
    )
  })
}
