# Changelog

Newest first. A release needs a `## <version> – <date>` heading here: its text becomes the release notes.

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
