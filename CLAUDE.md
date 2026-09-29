# R.A.D.A.R

Repo AI Discovery And Review: local tool that scans a directory of repositories, shows their AI setup
(CLAUDE.md, agents, skills, workflows, memory), computes coverage and gaps, and generates Claude Code / Codex CLI
commands to fill them. The MVP is **read-only**.

## Stack

- `src/Radar.Scanner` – .NET class library: repo discovery, detectors, coverage, gaps, progress events (stage 2).
- `src/Radar.Server` – ASP.NET Core minimal API, SSE, static hosting of the Angular build. Loopback only.
- `web/` – Angular (standalone components, signals, zoneless, Vitest). UI lives here.
- Storage: JSON files behind `IScanStore` (SQLite only if scan history is added later).

## Commands

```bash
# UI (from web/)
npm start                          # ng serve on http://localhost:4200, mock data
npm test -- --watch=false          # Vitest unit tests
npm run build                      # production build
node scripts/gen-mock.mjs          # regenerate web/public/mock/*.json

# Server / scanner (from repo root)
dotnet build
dotnet run --project src/Radar.Server   # http://127.0.0.1:5178
dotnet test                             # once test projects exist
```

Mock data: `?mock=150` or `?mock=400` in the URL loads the stress data sets for large rings.

## Conventions

- Talk to the user in Polish; the UI is in Polish; code, identifiers, commit messages and README are in English.
- Commit messages: a single sentence in past tense, no description body, no Co-Authored-By.
- Scanning is local and read-only. Nothing leaves the machine. No network calls at runtime (fonts are bundled).
- Never read secrets (`.env`, keys); only AI/markdown files.
- Any file access by path must be resolved and validated to be inside a detected repo (no symlink escapes).
- Server listens on `127.0.0.1` only; write endpoints do not exist in the MVP.
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
2. Scanner + JSON result + tests on fixtures.
3. Scan from the UI (SSE progress overlay, cache, "last scan", folder picker).
4. Read-only markdown preview + command generator for gaps.
5. Distribution (`run.sh`, GitHub Actions release binaries).

Requirements, mockup and the MVP spec live outside the repo (see `CLAUDE.local.md` if present).
