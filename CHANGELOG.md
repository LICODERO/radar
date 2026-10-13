# Changelog

Newest first. A release needs a `## <version> – <date>` heading here: its text becomes the release notes.

## Unreleased

- The row above the prompt now appears right after the plugin is added (it no longer waits for the next session), and `/radar:install` is the first step: it installs the app, then `/radar` does everything else (`start`, `stop`, `update`, `show`, `hide`, `uninstall`...).
- The row above the prompt shares its slot with other plugins' bands instead of replacing them (the band has one slot, and two plugins drawing there hid each other).
- The plugin works on Windows: PowerShell for start, stop, open, install and uninstall, `curl.exe` for the probes, and one set of path helpers so `/` and `\` are never mixed.

## 0.1.3 – 2026-10-12

- The Claude Code plugin is now a control panel: a row above the prompt with the version and a green or red dot (running or not), start/stop and open buttons, and a window with every command when you click the version. `/radar` runs `status`, `start`, `stop`, `restart`, `open`, `hide`/`show`, and `install`, `update` and `uninstall` without the model (the last three show a plan first and wait for `confirm`).
- The plugin's `/radar:install`, `/radar:start`, `/radar:update` and `/radar:uninstall` skills are gone: use `/radar install`, `/radar start`, `/radar update` and `/radar uninstall`.
- The installed program folder has a `VERSION` file, so the version can be read without starting it.
- A new private workflow is listed in `CLAUDE.local.md` right away (and kept in step when its visibility changes), and quality checks no longer call multi-line descriptions or a few long paragraphs "almost empty".
- The agent that installs R.A.D.A.R. for you installs the plugin in the same go when your request says so.

## 0.1.2 – 2026-10-09

First public release.

- Scans a folder of repositories and shows each repo's AI setup (CLAUDE.md, agents, skills, workflows) on an orbit HUD, with coverage, quality checks and gaps.
- Generates ready-to-run prompts for Claude Code (and opens a terminal after you confirm) to fill the gaps.
- Drafts new agents, skills and workflows from a description, copies shared ones between repos and keeps private ones out of git.
- Second brain: a vault of markdown notes outside your repositories, wired to each repo through CLAUDE.local.md.
- Flow between repos: a board where you connect the repositories that talk to each other, set how they authenticate and which way the data goes. It writes the rules to both repos and a pointer to CLAUDE.md, so an agent working in one repo knows to check the other.
- Everything runs locally; nothing is sent anywhere. Files are only written after you confirm.
- Installs with one sentence to your coding agent (it follows `AGENT_INSTALL.md` and verifies the checksum), or through the Claude Code plugin with `/radar:install`, `/radar:start`, `/radar:update` and `/radar:uninstall`.
- `radar uninstall`: lists what it would remove (the program, the `radar` link or PATH entry) and deletes only with `--confirm`; `--data` also forgets the settings, `--skill` removes the bundled skill. Repositories, the vault and shell profiles are never touched.
