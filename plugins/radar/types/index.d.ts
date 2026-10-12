export type Probe = { isRunning: boolean; version: string | null }

export type Facts = {
  isSupported: boolean
  probe: Probe
  /** `VERSION` next to the installed program; null when it is missing (installs made before the file existed) */
  installedVersion: string | null
  isInstalled: boolean
  /** set when the session runs inside a checkout of the R.A.D.A.R. repo */
  dev: { version: string | null; root: string } | null
}

declare module 'claude-code' {
  interface PluginState {
    radar: {
      /** what the row above the prompt draws; null until the first probe */
      facts: Facts | null
      /** the action in progress, so its button waits */
      busy: 'start' | 'stop' | null
      /** the person hid the row above the prompt (kept across sessions in the plugin's store) */
      isHidden: boolean
      /** the last answer of a command run from the commands pane */
      output: string | null
      /** a command whose plan the pane showed and waits for its confirm */
      pending: 'install' | 'update' | 'uninstall' | null
    }
  }
}
