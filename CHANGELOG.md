# Changelog

Newest first. A release needs a `## <version> – <date>` heading here: its text becomes the release notes.

## 0.1.1 – 2026-10-09

Test release of the installers.

- Rebuilt the release archives so the one-command and agent installers have a published release to download.

## 0.1.0 – 2026-10-07

First public release.

- Scans a folder of repositories and shows each repo's AI setup (CLAUDE.md, agents, skills, workflows) on an orbit HUD, with coverage, quality checks and gaps.
- Generates ready-to-run commands (and opens a terminal after you confirm) to fill the gaps with Claude Code or Codex CLI.
- Drafts new agents, skills and workflows from a description, copies shared ones between repos and keeps private ones out of git.
- Second brain: a vault of markdown notes outside your repositories, wired to each repo through CLAUDE.local.md.
- Flow between repos: a board where you draw which repo calls which, how it authenticates and which way the data goes. It writes the rules to both repos and a pointer to CLAUDE.md, so an agent working in one repo knows to check the other.
- Everything runs locally; nothing is sent anywhere. Files are only written after you confirm.
