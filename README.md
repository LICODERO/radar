# R.A.D.A.R

**Repo AI Discovery And Review** – a local-first dashboard that scans a directory of repositories for their AI setup (`CLAUDE.md`, agents, skills, workflows, memory), shows coverage and gaps, and generates Claude Code / Codex CLI commands to fill them.

> **Status:** early development. The UI mockup is done; the scanner and the app itself are not built yet. Everything under [Usage](#usage) describes the target behaviour.

## What it does

- Scans a chosen directory (default `~/projects`) and detects Git repositories.
- For each repository finds:
  - `CLAUDE.md`
  - agents – `.claude/agents/*.md`
  - skills – `.claude/skills/*/SKILL.md`
  - workflows – `.claude/workflows/*.md`
  - memory – `.claude/memory/OUTPUTS.md` and notes (detection and counters only)
- Detects the tech stack heuristically (.NET, Angular, Node, YAML, SQL).
- Computes AI coverage per repository and overall, and lists gaps (for example a missing `CLAUDE.md`).
- Shows everything in an "Orbit" HUD: repositories, agents, skills and workflows on concentric rings, with relations highlighted on selection.
- Lets you preview and edit markdown files in a side panel and create missing ones from templates.
- Generates ready-to-copy commands for Claude Code and Codex CLI for each gap.

### Coverage

Per repository, coverage is the sum of:

| Element | Weight |
|---|---|
| `CLAUDE.md` | 40 |
| at least one agent | 25 |
| at least one skill | 25 |
| `OUTPUTS.md` | 10 |

## Privacy and safety

- The scan is **local and read-only**. Nothing leaves your machine and the app works offline.
- Only AI/markdown files are read. Secrets such as `.env` files and keys are never touched.
- Files are written **only** after an explicit SAVE, and only inside detected repositories (paths are validated).
- The local server listens on `127.0.0.1` only and requires a per-session token, so other websites cannot reach it.

## Planned stack

- **Scanner and server:** .NET (ASP.NET Core minimal API), progress streamed to the UI with Server-Sent Events.
- **UI:** Angular (standalone components, signals), served by the same process.
- **Distribution:** a single self-contained binary per platform, no installer and no code signing.

The UI language is Polish; code and identifiers are in English.

## Usage

Planned; not available yet.

**From source** (requires the .NET SDK and Node.js):

```bash
git clone https://github.com/<owner>/radar.git
cd radar
./run.sh
```

**From a release binary:** download the archive for your platform from GitHub Releases and run `radar`. It starts a local server and opens the app in your browser.

Binaries are not code-signed. If macOS shows an "unverified developer" warning for a file downloaded in a browser, right-click the file and choose Open, or run:

```bash
xattr -d com.apple.quarantine ./radar
```

## Roadmap

1. Skeleton and the Orbit layout on mock data
2. Scanner (repositories, `CLAUDE.md`, agents, skills, stack, coverage, gaps)
3. Scan from the UI with real progress, cache and "last scan"
4. Workflows and OUTPUTS detection
5. Markdown editor with templates for new files
6. Command generator for gaps (Claude Code and Codex CLI)
7. Later: memory vault presentation, global agents/skills, scan history

## License

See [LICENSE](LICENSE).
