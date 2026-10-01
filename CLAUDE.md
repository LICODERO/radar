# R.A.D.A.R.

Repo AI Discovery And Review: local tool that scans a directory of repositories, shows their AI setup
(CLAUDE.md, agents, skills, workflows, memory), computes coverage and gaps, and generates Claude Code / Codex CLI
commands to fill them. The MVP is read-only, except for a few writes the user confirms (a new agent file, the second brain; see Conventions).

## Stack

- `src/api/Radar.Scanner` – .NET class library: repo discovery, detectors, coverage, gaps, progress events (`ScanEvent`).
- `src/api/Radar.Server` – ASP.NET Core minimal API, SSE, JSON state files, static hosting of the Angular build. Loopback only.
  Vertical slices: `Features/<Name>/` (Session, Settings, Scans, Files, Gaps, Agents, Vault) each hold their endpoints (`<Name>Endpoints.cs`,
  a `Map<Name>()` extension called from `Program.cs`), request records and logic; `Infrastructure/` holds Security and Storage. Namespaces follow folders (`Radar.Server.Features.Scans`, `Radar.Server.Infrastructure.Storage`...).
- `src/tests/Radar.Scanner.Tests`, `src/tests/Radar.Server.Tests` – xUnit; fixtures are built in temp dirs (repos cannot be committed inside a repo).
- `src/web/` – Angular (standalone components, signals, zoneless, Vitest). UI lives here.
- Storage: JSON files behind `IScanStore` (SQLite only if scan history is added later).

## Commands

```bash
./run.sh                           # build UI if needed + start the server on http://127.0.0.1:5178 (Windows: ./run.ps1)
./dev.sh                           # dotnet watch + ng serve (http://localhost:4200, /api proxied)
dotnet build
dotnet test                        # scanner + server tests

# UI (from src/web/)
npm start                          # ng serve; add ?mock (or ?mock=150 / ?mock=400) to use sample data without the server
npm test -- --watch=false          # Vitest
npm run build
node scripts/gen-mock.mjs          # regenerate src/web/public/mock/*.json
```

State lives in the data dir (`~/Library/Application Support/RADAR`): `settings.json`, `scan-result.json`.
Override with `Radar__DataDir` (tests and manual experiments must never touch the real one).

## API (all under /api, loopback only)

`GET session` (token) · `GET/PUT settings` · `POST pick-folder` · `GET scan/latest` · `GET file?repo=&path=` (read-only preview) · `GET gaps` (commands/prompts, server is the single source) · `POST agents/generate` (draft) · `POST agents` (create) · `GET/POST/PUT vault` (second brain status / create / relink) · `GET/POST vault/skill` (status / install the bundled skill) · `POST vault/projects` (second brain for one repo; preview unless `confirm`) · `GET tools` (platform, shell, claude/codex on PATH) · `POST run` · `POST scans` · `GET scans/current` ·
`GET scans/{id}/events` (SSE: started, phase, repo-found, repo-scanned, completed, cancelled, error) · `DELETE scans/{id}`.
`run` opens iTerm2 or Terminal.app (macOS: iTerm2 when it is installed, force one with `Radar__Terminal=terminal|iterm|auto`) or PowerShell (Windows) in the repo and starts `claude`/`codex` with the gap prompt; the client sends only repo id + gap type + tool, the server builds the command (never accept command text from the client). The session is interactive, the app itself writes nothing into repos.
`file` only serves paths the latest scan reported for that repo (CLAUDE.md, agents, skills, workflows), `.md` only, max 256 KB, never through a symlink leaving the repo.
Everything except `session`/`health` needs `X-Radar-Token` (or `?token=` for SSE); Host must be loopback; foreign Origins are rejected.

## Conventions

- Version: one place, `Directory.Build.props` (`<Version>`); the server returns it in `GET session` and the UI shows it in the bottom-right About line/dialog. Bump it there for a release.
- The project name is written **R.A.D.A.R.** with the trailing dot everywhere (UI, README, scripts, docs, messages). Identifiers and paths (`Radar.*`, the `RADAR` data folder) are unaffected.
- Talk to the user in Polish; code, identifiers, commit messages and README are in English.
- i18n: the UI is bilingual (PL/EN, switch in the header, choice kept in localStorage `radar.lang`, default from the browser language, Polish fallback).
  Every UI string lives in `src/web/src/app/i18n/pl.ts` (source, defines the keys) and `en.ts` (must match; a spec checks keys and `{placeholders}`);
  use `inject(I18n).t('key', { params })` (plurals: message object with `one/few/many/other`, pick with `n`), never hard-coded text in templates.
  The client sends `Accept-Language`; API error texts come from `src/api/Radar.Server/Infrastructure/Localization/Messages.cs` (`Msg` enum, PL default).
- Commit messages: a single sentence in past tense, no description body, no Co-Authored-By.
- Scanning is local and read-only. Nothing leaves the machine. No network calls at runtime (fonts are bundled).
- Never read secrets (`.env`, keys); only AI/markdown files.
- Any file access by path must be resolved and validated to be inside a detected repo (no symlink escapes).
- Cross-platform: macOS and Windows are supported (Linux: everything except opening a terminal). Windows code paths (PowerShell launcher, folder dialog, `run.ps1`) are covered by unit tests of the builders but were not run on a real Windows machine yet.
- Drafting an agent from a description (`POST agents/generate`) runs `claude -p` with no tools, `--strict-mcp-config --setting-sources project`, in an empty temp dir, and sends only the description, the stack name and existing agent names. Measured on the real CLI: ~0.06 cent per call with these flags vs ~5 cents without. `POST agents` is the only place the app writes into a repo: one new `.claude/agents/<name>.md`, only after the user confirms, never overwriting.
- Server listens on `127.0.0.1` only. Writes into a repo are limited to two confirmed ones: a new `.claude/agents/<name>.md` and the RADAR block in `CLAUDE.local.md` (second brain); everything else in repos is read-only (plus the app's own settings/cache).
- Second brain (`Features/Vault/`): a vault folder outside every repo (never inside a git repo; the check stops at the home dir), holding `.radar-vault.json` (marker, trusted only while present), `_indexMain.md` (project list between `radar:projects` markers + how to use the vault) and one folder per project (`raw/` read-only sources, `memory/` wiki, `outputs/`, `_index.md`). The path is `VaultPath` in `settings.json`; `GET vault` reports `none` / `ok` / `missing` (folder moved), and `PUT vault` relinks a moved folder (RADAR never moves files). `POST vault` needs an empty or new folder. `POST vault/projects` without `confirm` only returns a plan; with it, it creates the project folder (never overwriting) and upserts a block between `<!-- radar:second-brain -->` markers in the repo's `CLAUDE.local.md` (damaged markers, a link leading out of the repo, or a name clash refuse the write). The vault and CLAUDE.local.md texts are templates embedded in the server (`Features/Vault/Templates/`); the client never supplies them. RADAR does not read the vault content and no longer looks for `OUTPUTS.md` in repos.
- Bundled skill `radar-second-brain` (`SecondBrainSkill`): installed on request into `<CLAUDE_CONFIG_DIR or ~/.claude>/skills/radar-second-brain/SKILL.md` with a marker line (version + hash of the text above it). It never overwrites: a skill that is linked (also `skills/` itself), foreign or edited by hand is left alone; only an untouched older copy is updated, and only with `update: true`. Tests point `Radar:ClaudeDir` at a temp dir, so they never touch the real `~/.claude` (like `Radar__DataDir`).
- Layout: fixed 1440x900 stage scaled proportionally (`src/web/src/app/core/fit-scale.ts`, minimum scale 0.85, smaller windows scroll); use `fs(px)` from
  `src/web/src/styles/_tokens.scss` for every font size so fonts stay within 0.85x-1.2x.
- Palette: lime `#c6ff3d` (agents), violet `#a99bff` (skills), white `#e6e9f2` (repos), cyan `#4dd6ff` (workflows),
  magenta `#ff4fa3` (gaps), amber `#ffb84d` (memory). Repo colour by coverage: >=70 white, 40-69 amber, <40 magenta.
- Orbit geometry is pure functions in `src/web/src/app/orbit/` with unit tests; keep DOM code thin.
- Respect `prefers-reduced-motion`.

## Domain

- Repo = directory with `.git` under the scan path (recursive, default depth 4, skip `node_modules`, `bin`, `obj`, `.git`, `dist`...).
- Detected per repo: `CLAUDE.md`, `.claude/agents/*.md`, `.claude/skills/*/SKILL.md`, `.claude/workflows/*.md`.
- Workflow = per-project procedure for agents (how to code an API, check UI, commit...). Repo-scoped for now: it only relates to the agents/skills of the repo it lives in (orchestrator-level workflows are a later stage). Frontmatter: `name`,
  `description`, `when`, optional `agents` and `skills` (names of the agents/skills the procedure uses; picking a workflow in the UI lights them up). Same `name` across repos aggregates into one node (W1...).
- Coverage: CLAUDE.md 40 + agents(>=1) 30 + skills(>=1) 30.
- Gaps: `no-claude-md` (counted in the KPI), `no-agents`, `no-skills`, `workflow-not-linked`.

## Stages

1. Skeleton + Orbit layout on mock data (done).
2. Scanner + JSON result + tests on fixtures (done).
3. Scan from the UI: SSE progress overlay, saved result, "last scan", folder picker (done).
4. Read-only markdown preview + command generator for gaps (done), plus URUCHOM: opens a terminal after an explicit confirmation. Prompts live in `src/api/Radar.Server/Features/Gaps/GapCommands.cs`; the UI formats commands per shell in `src/web/src/app/core/commands.ts`.
5. Distribution (GitHub Actions release binaries).
6. Second brain: vault logic, bundled skill and per-repo enabling on the server, and their UI in `src/web/src/app/vault/` (`vault-dialog`: intro / location / manage / relink + skill install, `project-dialog`: preview then confirm; opened from the right panel) are done; next repo relations (`relations.json`, names only, repos as siblings, export/import) and their block in `CLAUDE.local.md`.

Requirements, mockup and the MVP spec live outside the repo (see `CLAUDE.local.md` if present).
