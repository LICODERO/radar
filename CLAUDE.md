# R.A.D.A.R

Repo AI Discovery And Review: local tool that scans a directory of repositories, shows their AI setup
(CLAUDE.md, agents, skills, workflows, memory), computes coverage and gaps, and generates Claude Code / Codex CLI
commands to fill them. The MVP is **read-only**.

## Stack

- `src/Radar.Scanner` – .NET class library: repo discovery, detectors, coverage, gaps, progress events (`ScanEvent`).
- `src/Radar.Server` – ASP.NET Core minimal API, SSE, JSON state files, static hosting of the Angular build. Loopback only.
- `tests/Radar.Scanner.Tests`, `tests/Radar.Server.Tests` – xUnit; fixtures are built in temp dirs (repos cannot be committed inside a repo).
- `web/` – Angular (standalone components, signals, zoneless, Vitest). UI lives here.
- Storage: JSON files behind `IScanStore` (SQLite only if scan history is added later).

## Commands

```bash
./run.sh                           # build UI if needed + start the server on http://127.0.0.1:5178 (Windows: ./run.ps1)
./dev.sh                           # dotnet watch + ng serve (http://localhost:4200, /api proxied)
dotnet build
dotnet test                        # scanner + server tests

# UI (from web/)
npm start                          # ng serve; add ?mock (or ?mock=150 / ?mock=400) to use sample data without the server
npm test -- --watch=false          # Vitest
npm run build
node scripts/gen-mock.mjs          # regenerate web/public/mock/*.json
```

State lives in the data dir (`~/Library/Application Support/RADAR`): `settings.json`, `scan-result.json`.
Override with `Radar__DataDir` (tests and manual experiments must never touch the real one).

## API (all under /api, loopback only)

`GET session` (token) · `GET/PUT settings` · `POST pick-folder` · `GET scan/latest` · `GET file?repo=&path=` (read-only preview) · `GET gaps` (commands/prompts, server is the single source) · `GET tools` (platform, shell, claude/codex on PATH) · `POST run` · `POST scans` · `GET scans/current` ·
`GET scans/{id}/events` (SSE: started, phase, repo-found, repo-scanned, completed, cancelled, error) · `DELETE scans/{id}`.
`run` opens Terminal.app (macOS) or PowerShell (Windows) in the repo and starts `claude`/`codex` with the gap prompt; the client sends only repo id + gap type + tool, the server builds the command (never accept command text from the client). The session is interactive, the app itself writes nothing into repos.
`file` only serves paths the latest scan reported for that repo (CLAUDE.md, agents, skills, workflows), `.md` only, max 256 KB, never through a symlink leaving the repo.
Everything except `session`/`health` needs `X-Radar-Token` (or `?token=` for SSE); Host must be loopback; foreign Origins are rejected.

## Conventions

- Talk to the user in Polish; the UI is in Polish; code, identifiers, commit messages and README are in English.
- Commit messages: a single sentence in past tense, no description body, no Co-Authored-By.
- Scanning is local and read-only. Nothing leaves the machine. No network calls at runtime (fonts are bundled).
- Never read secrets (`.env`, keys); only AI/markdown files.
- Any file access by path must be resolved and validated to be inside a detected repo (no symlink escapes).
- Cross-platform: macOS and Windows are supported (Linux: everything except opening a terminal). Windows code paths (PowerShell launcher, folder dialog, `run.ps1`) are covered by unit tests of the builders but were not run on a real Windows machine yet.
- Server listens on `127.0.0.1` only; there are no endpoints that write into repos in the MVP (only the app's own settings/cache).
- Layout: fixed 1440x900 stage scaled proportionally (`web/src/app/core/fit-scale.ts`, minimum scale 0.85, smaller windows scroll); use `fs(px)` from
  `web/src/styles/_tokens.scss` for every font size so fonts stay within 0.85x-1.2x.
- Palette: lime `#c6ff3d` (agents), violet `#a99bff` (skills), white `#e6e9f2` (repos, workflows),
  magenta `#ff4fa3` (gaps), amber `#ffb84d` (memory). Repo colour by coverage: >=70 white, 40-69 amber, <40 magenta.
- Orbit geometry is pure functions in `web/src/app/orbit/` with unit tests; keep DOM code thin.
- Respect `prefers-reduced-motion`.

## Domain

- Repo = directory with `.git` under the scan path (recursive, default depth 4, skip `node_modules`, `bin`, `obj`, `.git`, `dist`...).
- Detected per repo: `CLAUDE.md`, `.claude/agents/*.md`, `.claude/skills/*/SKILL.md`, `.claude/workflows/*.md`,
  `.claude/memory/OUTPUTS.md` (+ notes in `.claude/memory/`).
- Workflow = per-project procedure for agents (how to code an API, check UI, commit...). Frontmatter: `name`,
  `description`, `when`, optional `agents`. Same `name` across repos aggregates into one node (W1...).
- Coverage: CLAUDE.md 40 + agents(>=1) 25 + skills(>=1) 25 + OUTPUTS 10.
- Gaps: `no-claude-md` (counted in the KPI), `no-agents`, `no-skills`, `no-outputs`, `workflow-not-linked`.

## Stages

1. Skeleton + Orbit layout on mock data (done).
2. Scanner + JSON result + tests on fixtures (done).
3. Scan from the UI: SSE progress overlay, saved result, "last scan", folder picker (done).
4. Read-only markdown preview + command generator for gaps (done), plus URUCHOM: opens a terminal after an explicit confirmation. Prompts live in `src/Radar.Server/GapCommands.cs`; the UI formats commands per shell in `web/src/app/core/commands.ts`.
5. Distribution (GitHub Actions release binaries).

Requirements, mockup and the MVP spec live outside the repo (see `CLAUDE.local.md` if present).
