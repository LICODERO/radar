# R.A.D.A.R.

**Repo AI Discovery And Review** – a local-first dashboard that scans a directory of repositories for their AI setup (`CLAUDE.md`, agents, skills, workflows, memory), shows coverage and gaps, and generates Claude Code / Codex CLI commands to fill them.

> **Status:** early development (read-only MVP in progress). Scanning, folder selection, live scan progress, saved results, read-only file preview and the command generator work.

## What it does

- Scans a chosen directory (default `~/projects`) and detects Git repositories.
- For each repository finds:
  - `CLAUDE.md`
  - agents – `.claude/agents/*.md`
  - skills – `.claude/skills/*/SKILL.md`
  - workflows – `.claude/workflows/*.md`
- Detects the tech stack heuristically (.NET, Angular, Node, YAML, SQL).
- Computes AI coverage per repository and overall, and lists gaps (for example a missing `CLAUDE.md`).
- Rates the quality of the files that exist, not just their presence: a thin or stale `CLAUDE.md`, one that points at files which are gone, agents without a usable description, skills that cannot be triggered. Checks are local heuristics; Claude Code can fix what they find.
- After a rescan it tells you whether and how the average coverage changed. It also shows which agents and skills live in several repositories with copies that drifted apart, and lets you copy an agent from its panel to other repositories after you confirm; existing files are never overwritten.
- Shows everything in an "Orbit" HUD: repositories, agents, skills and workflows on concentric rings, with relations highlighted on selection.
- Lets you preview and edit markdown files in a side panel and create missing ones from templates.
- Generates ready-to-copy commands for Claude Code and Codex CLI for each gap.

### Coverage

Per repository, coverage is the sum of:

| Element | Weight |
|---|---|
| `CLAUDE.md` | 40 |
| at least one agent | 30 |
| at least one skill | 30 |

## Privacy and safety

- The scan is **local and read-only**. Scanning sends nothing anywhere and works offline; only the optional agent drafting calls `claude`.
- Only AI/markdown files are read. Secrets such as `.env` files and keys are never touched.
- The only thing the app writes into a repository is a new agent file, after an explicit SAVE and only inside detected repositories (paths are validated, existing files are never overwritten). Drafting sends your description to Claude through your own CLI; nothing else leaves your machine.
- The local server listens on `127.0.0.1` only and requires a per-session token, so other websites cannot reach it.

## Planned stack

- **Scanner and server:** .NET (ASP.NET Core minimal API), progress streamed to the UI with Server-Sent Events.
- **UI:** Angular (standalone components, signals), served by the same process.
- **Distribution:** a single self-contained binary per platform, no installer and no code signing.

The UI is available in Polish and English (switch in the header); code and identifiers are in English.

## Usage

**From source** (requires the .NET SDK 10 and Node.js 22+; on Windows use `./run.ps1` instead of `./run.sh`):

```bash
git clone https://github.com/<owner>/radar.git
cd radar
./run.sh
```

`run.sh` builds the UI on the first run (`./run.sh --rebuild` to rebuild), starts the server on `http://127.0.0.1:5178` and opens it in your browser. Then:

1. Click **ZMIEŃ** and pick the directory that contains your repositories (macOS shows a native folder dialog). Picking a directory starts the scan right away.
2. The overlay shows real progress from the scanner; when it finishes, close it to see the dashboard. Use **SKANUJ PONOWNIE** to rescan the same directory.
3. Click an agent, skill or workflow (or **OTWÓRZ CLAUDE.md**) and choose **OTWÓRZ PLIK** for a read-only preview of the file.
4. **+ AGENT** (left panel) drafts a new agent from a description in your own words: it calls your `claude` CLI once, with no tools and in an empty temporary directory, sending only your description, the stack name and existing agent names. You review and edit the draft; **ZAPISZ** then creates `.claude/agents/<name>.md` (never overwriting an existing file).
5. **GENERUJ POLECENIA** lists ready-made `claude` / `codex` commands for the gaps (missing `CLAUDE.md`, agents, skills, unlinked workflows). **KOPIUJ** copies a command (POSIX or PowerShell flavour, depending on your system); **URUCHOM** opens iTerm2 or Terminal.app (macOS; iTerm2 when installed, override with `Radar__Terminal=terminal` or `iterm`) or PowerShell (Windows) in the repo and starts `claude` / `codex` with the prompt after you confirm. The session is interactive, so you approve every change in the tool; the app itself never writes into your repos.

6. **SECOND BRAIN** (right panel) sets up a vault: a folder of markdown notes (`raw/`, `memory/`, `outputs/` per project) that lives outside your repositories, so nothing from it is committed. The wizard creates it in an empty folder (or offers a subfolder), and you can point R.A.D.A.R. to a new place if you move it. It can also install the bundled `radar-second-brain` skill into your Claude Code skills folder, only on your click and never over an existing file. **+ FOR THIS REPO** shows what it would write, and after you confirm it creates the project folder in the vault and adds a marked block with the vault path to that repo's `CLAUDE.local.md` (keep that file out of git). This is the only other write besides new agent files.

7. **FLOW BETWEEN REPOS** (bottom left) is a wizard for how your repositories talk to each other. Put repos on a board, drag a connection from the caller to the called repo and describe it: kind (REST, gRPC, events...), authentication (API key, OAuth client id and secret...), the *names* of the environment variables that hold the secrets (never the values), the contract and notes. After a preview and your confirmation R.A.D.A.R. writes the rule to `.claude/relations.md` of **both** repos (or `.claude/relations.local.md`, kept out of git, for private rules) and adds a short pointer to `CLAUDE.md` (or `CLAUDE.local.md`). An agent working on a feature in one repo then knows it has to look at the other repo, work through its agent and ask before changing anything there.

The last scan and your settings are saved in `~/Library/Application Support/RADAR` (`%APPDATA%\RADAR` on Windows, `~/.local/share/radar` on Linux), so the next start shows the previous result immediately.

**Development:** `./dev.sh` runs the .NET server with hot reload and the Angular dev server (`http://localhost:4200`). The UI can also run on sample data without the server: `cd src/web && npm start`, then open `http://localhost:4200/?mock` (or `?mock=150` / `?mock=400` for large data sets).

**Tests:** `dotnet test` (scanner and server) and `cd src/web && npm test -- --watch=false` (UI).

**Release binaries** (planned): download the archive for your platform from GitHub Releases and run `radar`.

Binaries are not code-signed. If macOS shows an "unverified developer" warning for a file downloaded in a browser, right-click the file and choose Open, or run:

```bash
xattr -d com.apple.quarantine ./radar
```

## Roadmap

1. ~~Skeleton and the Orbit layout on mock data~~
2. ~~Scanner (repositories, `CLAUDE.md`, agents, skills, workflows, stack, coverage, gaps)~~
3. ~~Scan from the UI with real progress, saved result and "last scan"~~
4. ~~Read-only markdown preview and command generator for gaps (Claude Code and Codex CLI)~~
5. Distribution (release binaries)
6. Later: markdown editor with templates for new files, memory vault presentation, global agents/skills, scan history

## License

See [LICENSE](LICENSE).
