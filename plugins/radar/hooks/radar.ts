// Pure helpers of the radar plugin: no engine calls, so the tests can run them as they are.

export const PORT = 5178
export const RAW = 'https://raw.githubusercontent.com/LICODERO/radar/main'
export const BASE_URL = `http://127.0.0.1:${PORT}`

export type { Facts, Probe } from '../types'
import type { Facts } from '../types'

/** `windows` when the session runs on Windows (paths with `\\`, PowerShell), else `unix` (macOS, Linux). */
export type Os = 'windows' | 'unix'

/** `OS` is `Windows_NT` in every Windows process and unset elsewhere. */
export function osOf(osVar: string | undefined): Os {
  return osVar === 'Windows_NT' ? 'windows' : 'unix'
}

/** Joins path parts with the system's separator and mixes of `/` and `\\` become one kind, so a path never holds both. */
export function joinPath(os: Os, ...parts: string[]): string {
  const sep = os === 'windows' ? '\\' : '/'
  const joined = parts
    .filter(p => p !== '')
    .join(sep)
    .replace(/[\\/]+/g, sep)
  // a UNC share (\\\\host\\share) keeps its two leading separators
  const isUnc = os === 'windows' && parts[0]?.startsWith('\\\\') === true
  return isUnc ? `${sep}${joined}` : joined
}

/** The folder holding a path, for either separator; '' when there is none above. */
export function parentDir(path: string): string {
  const trimmed = path.replace(/[\\/]+$/, '')
  const i = Math.max(trimmed.lastIndexOf('/'), trimmed.lastIndexOf('\\'))
  if (i < 0) return ''
  // the root of a drive (C:\) or of the file system (/) stays
  if (i === 0) return trimmed.slice(0, 1)
  if (/^[A-Za-z]:$/.test(trimmed.slice(0, i))) return `${trimmed.slice(0, i)}\\`
  return trimmed.slice(0, i)
}

/** Where the installer puts R.A.D.A.R.: `RADAR_HOME`, else `~/.radar` or `%LOCALAPPDATA%\\RADAR` (the install scripts' own defaults). */
export function homeOf(os: Os, env: { custom?: string; home?: string; userProfile?: string; localAppData?: string }): string {
  if (env.custom) return joinPath(os, env.custom)
  if (os === 'windows') {
    const local = env.localAppData || joinPath(os, env.userProfile ?? '', 'AppData', 'Local')
    return joinPath(os, local, 'RADAR')
  }
  return joinPath(os, env.home ?? '', '.radar')
}

/** The program the installer unpacks into `<home>/app`. */
export function binaryOf(os: Os, home: string): string {
  return joinPath(os, home, 'app', os === 'windows' ? 'radar.exe' : 'radar')
}

/** The launcher script of a checkout, and the arguments a spawned `powershell` needs to run it (none on unix). */
export function runScriptOf(os: Os, root: string): { file: string; args: string } {
  if (os === 'unix') return { file: joinPath(os, root, 'run.sh'), args: '' }
  return { file: 'powershell.exe', args: `-NoProfile -ExecutionPolicy Bypass -File "${joinPath(os, root, 'run.ps1')}"` }
}

/** argv that runs a PowerShell script; its values travel in the environment, never spliced into the text. */
export function powershell(script: string): string[] {
  return ['powershell.exe', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', script]
}

const SEMVER = /^\d+\.\d+\.\d+(?:[-+][\w.]+)?$/

/** The first line of a VERSION file, or null when it does not hold a version. */
export function parseVersionFile(text: string | null): string | null {
  const line = text?.split(/\r?\n/)[0]?.trim() ?? ''
  return SEMVER.test(line) ? line : null
}

/** `<Version>` of Directory.Build.props. */
export function parseProps(text: string | null): string | null {
  const m = text?.match(/<Version>\s*([^<\s]+)\s*<\/Version>/)
  return m && SEMVER.test(m[1]) ? m[1] : null
}

/** `{"status":"ok","app":"R.A.D.A.R."}` from /api/health. */
export function isRadarHealth(text: string): boolean {
  try {
    const body = JSON.parse(text) as { status?: string; app?: string }
    return body.status === 'ok' && body.app === 'R.A.D.A.R.'
  } catch {
    return false
  }
}

/** `version` of /api/session. */
export function parseSessionVersion(text: string): string | null {
  try {
    const v = (JSON.parse(text) as { version?: unknown }).version
    return typeof v === 'string' && SEMVER.test(v) ? v : null
  } catch {
    return null
  }
}

/** What the row above the prompt draws; `isVisible` false means it stays out of the way (not supported, nothing to control). */
export type Band = { isVisible: boolean; version: string | null; isRunning: boolean; canStart: boolean }

export function bandOf(f: Facts): Band {
  const isVisible = f.isSupported && (f.probe.isRunning || f.isInstalled || f.dev !== null)
  const version = f.probe.isRunning ? f.probe.version : (f.installedVersion ?? f.dev?.version ?? null)
  return { isVisible, version, isRunning: f.probe.isRunning, canStart: f.isInstalled || f.dev !== null }
}

export function describe(f: Facts): string {
  if (!f.isSupported) return 'R.A.D.A.R.: nie umiem ustalić systemu, na którym działa ta sesja.'
  const lines: string[] = []
  lines.push(f.probe.isRunning ? `Działa: ${BASE_URL}` : 'Nie działa.')
  if (f.probe.isRunning) lines.push(`  uruchomiona wersja: ${f.probe.version ?? 'nieznana'}`)
  if (f.isInstalled) lines.push(`  zainstalowana:      ${f.installedVersion ?? 'nieznana (instalacja sprzed pliku VERSION)'}`)
  else lines.push('  zainstalowana:      brak (/radar install)')
  if (f.dev) lines.push(`  to repo (źródła):   ${f.dev.version ?? 'nieznana'}`)
  if (f.probe.isRunning && f.installedVersion && f.probe.version && f.installedVersion !== f.probe.version)
    lines.push(`  uwaga: działa ${f.probe.version}, a zainstalowana jest ${f.installedVersion}; to nie jest ta sama kopia.`)
  return lines.join('\n')
}

/** What `/radar start` runs: the repo's run.sh inside a checkout (unless asked otherwise), else the installed program. */
export function pickSource(arg: string, f: Facts): 'dev' | 'installed' | 'none' {
  if (arg === 'installed') return f.isInstalled ? 'installed' : 'none'
  if (arg === 'dev') return f.dev ? 'dev' : 'none'
  if (f.dev) return 'dev'
  return f.isInstalled ? 'installed' : 'none'
}

/** The commands the pane lists, in the order it lists them. `hide` stands for hide, show and toggle. */
export const COMMANDS: readonly { verb: string; hint: string }[] = [
  { verb: 'status', hint: 'stan, wersje, adres' },
  { verb: 'start', hint: 'uruchom (w repo ze źródeł, inaczej zainstalowaną kopię)' },
  { verb: 'stop', hint: 'zatrzymaj' },
  { verb: 'restart', hint: 'zatrzymaj i uruchom ponownie' },
  { verb: 'open', hint: 'otwórz w przeglądarce' },
  { verb: 'hide', hint: 'ukryj panel (przywróć: /radar show)' },
  { verb: 'install', hint: 'zainstaluj z najnowszego wydania' },
  { verb: 'update', hint: 'zaktualizuj do najnowszego wydania' },
  { verb: 'uninstall', hint: 'usuń zainstalowaną kopię' },
]

export const USAGE = 'Użycie: /radar [status | start [dev|installed] | stop | restart | open | hide | show | toggle | install | update | uninstall]'

/** -1, 0 or 1 by the numeric parts of two versions (a pre-release suffix is ignored). */
export function compareVersions(a: string, b: string): number {
  const part = (v: string) => v.split(/[-+]/)[0].split('.').map(n => Number(n) || 0)
  const x = part(a)
  const y = part(b)
  for (let i = 0; i < 3; i++) {
    const d = (x[i] ?? 0) - (y[i] ?? 0)
    if (d !== 0) return d < 0 ? -1 : 1
  }
  return 0
}

/** The release tag of GitHub's `releases/latest` answer, without the v. */
export function parseLatestTag(text: string): string | null {
  try {
    const tag = (JSON.parse(text) as { tag_name?: unknown }).tag_name
    return typeof tag === 'string' && SEMVER.test(tag.replace(/^v/, '')) ? tag.replace(/^v/, '') : null
  } catch {
    return null
  }
}

/** What an install or update says before anything is downloaded. */
export function installPlan(verb: 'install' | 'update', f: Facts, latest: string | null, os: Os = 'unix'): string {
  const lines: string[] = []
  lines.push(verb === 'update' ? 'Aktualizacja R.A.D.A.R.' : 'Instalacja R.A.D.A.R.')
  lines.push(`  zainstalowana: ${f.isInstalled ? (f.installedVersion ?? 'nieznana') : 'brak'}`)
  lines.push(`  najnowsza:     ${latest ?? 'nie udało się sprawdzić'}`)
  if (f.installedVersion && latest && compareVersions(f.installedVersion, latest) >= 0) lines.push('  Masz już najnowszą wersję.')
  lines.push(
    os === 'windows'
      ? `Pobiorę ${RAW}/install.ps1 i uruchomię go. Skrypt pobiera archiwum z najnowszego wydania LICODERO/radar, sprawdza SHA256 (przy niezgodności przerywa) i rozpakowuje do %LOCALAPPDATA%\\RADAR\\app. Bez uprawnień administratora.`
      : `Pobiorę ${RAW}/install.sh i uruchomię go. Skrypt pobiera archiwum z najnowszego wydania LICODERO/radar, sprawdza SHA256 (przy niezgodności przerywa), rozpakowuje do ~/.radar/app i linkuje ~/.local/bin/radar. Bez sudo.`,
  )
  if (verb === 'update' && f.probe.isRunning) lines.push('Działającą kopię zatrzymam przed aktualizacją.')
  lines.push(`Potwierdź: /radar ${verb} confirm`)
  return lines.join('\n')
}
