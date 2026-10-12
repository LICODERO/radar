import { expect, test } from 'claude-code/testing'

import { bandOf, compareVersions, describe, installPlan, isRadarHealth, parseLatestTag, parseProps, parseSessionVersion, parseVersionFile, pickSource } from './radar'
import type { Facts } from '../types'

const base: Facts = {
  isSupported: true,
  probe: { isRunning: false, version: null },
  installedVersion: null,
  isInstalled: false,
  dev: null,
}

test('reads versions from the VERSION file, the props and the session', () => {
  expect(parseVersionFile('0.1.2\n')).toBe('0.1.2')
  expect(parseVersionFile('0.1.2\r\n')).toBe('0.1.2')
  expect(parseVersionFile('<html>')).toBeNull()
  expect(parseVersionFile(null)).toBeNull()
  expect(parseProps('<Project><PropertyGroup><Version>0.1.2</Version></PropertyGroup></Project>')).toBe('0.1.2')
  expect(parseProps('<Project/>')).toBeNull()
  expect(parseSessionVersion('{"token":"t","version":"0.1.2"}')).toBe('0.1.2')
  expect(parseSessionVersion('nope')).toBeNull()
})

test('only R.A.D.A.R.\'s own health answer counts as running', () => {
  expect(isRadarHealth('{"status":"ok","app":"R.A.D.A.R."}')).toBe(true)
  expect(isRadarHealth('{"status":"ok","app":"other"}')).toBe(false)
  expect(isRadarHealth('')).toBe(false)
})

test('the row above the prompt shows version and state, and stays out of the way with nothing to control', () => {
  expect(bandOf(base).isVisible).toBe(false)
  expect(bandOf({ ...base, isSupported: false, isInstalled: true }).isVisible).toBe(false)

  const stopped = bandOf({ ...base, isInstalled: true, installedVersion: '0.1.2' })
  expect(stopped).toEqual({ isVisible: true, version: '0.1.2', isRunning: false, canStart: true })

  const running = bandOf({ ...base, probe: { isRunning: true, version: '0.1.3' }, installedVersion: '0.1.2' })
  expect(running).toEqual({ isVisible: true, version: '0.1.3', isRunning: true, canStart: false })

  expect(bandOf({ ...base, dev: { version: '0.2.0', root: '/src/radar' } })).toEqual({ isVisible: true, version: '0.2.0', isRunning: false, canStart: true })
  expect(bandOf({ ...base, isInstalled: true }).version).toBeNull()
})

test('status warns when the running copy is not the installed one', () => {
  const text = describe({ ...base, isInstalled: true, installedVersion: '0.1.2', probe: { isRunning: true, version: '0.2.0' } })
  expect(text).toContain('Działa: http://127.0.0.1:5178')
  expect(text).toContain('nie jest ta sama kopia')
})

test('start prefers the checkout and falls back to the installed copy', () => {
  const dev = { version: '0.2.0', root: '/src/radar' }
  expect(pickSource('', { ...base, dev, isInstalled: true })).toBe('dev')
  expect(pickSource('installed', { ...base, dev, isInstalled: true })).toBe('installed')
  expect(pickSource('', { ...base, isInstalled: true })).toBe('installed')
  expect(pickSource('dev', { ...base, isInstalled: true })).toBe('none')
  expect(pickSource('', base)).toBe('none')
})

test('versions compare by number, not by text', () => {
  expect(compareVersions('0.1.2', '0.1.10')).toBe(-1)
  expect(compareVersions('1.0.0', '0.9.9')).toBe(1)
  expect(compareVersions('0.1.2', '0.1.2')).toBe(0)
  expect(compareVersions('0.1.2-rc1', '0.1.2')).toBe(0)
})

test('the latest release comes from the tag of the GitHub answer', () => {
  expect(parseLatestTag('{"tag_name":"v0.1.3"}')).toBe('0.1.3')
  expect(parseLatestTag('{"message":"Not Found"}')).toBeNull()
  expect(parseLatestTag('')).toBeNull()
})

test('an update plan names the source, the versions and how to confirm', () => {
  const plan = installPlan('update', { ...base, isInstalled: true, installedVersion: '0.1.2', probe: { isRunning: true, version: '0.1.2' } }, '0.1.3')
  expect(plan).toContain('0.1.2')
  expect(plan).toContain('0.1.3')
  expect(plan).toContain('install.sh')
  expect(plan).toContain('zatrzymam')
  expect(plan).toContain('/radar update confirm')
  expect(installPlan('update', { ...base, isInstalled: true, installedVersion: '0.1.3' }, '0.1.3')).toContain('najnowszą')
})
