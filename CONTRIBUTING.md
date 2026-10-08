# Contributing to R.A.D.A.R.

Thanks for taking a look. Bug reports, ideas and pull requests are welcome. This is a one-person project run in spare time, so replies can take a few days.

## Before you start

- **Bugs and ideas:** [open an issue](https://github.com/lookashdev/radar/issues/new/choose). The templates ask for your system and the app version.
- **Small fixes** (typos, a clear bug with a test): send a pull request straight away.
- **Bigger changes** (a new feature, anything that writes into a user's repositories or runs commands): open an issue first so we agree on the approach. R.A.D.A.R. touches other people's repositories, so those parts are reviewed with extra care. See [SECURITY.md](SECURITY.md) for the rules the app keeps.

## Set up

You need the .NET SDK 10 and Node.js 22.

```bash
./run.sh                    # build the UI if needed and start the app on http://127.0.0.1:5178
./dev.sh                    # dotnet watch + ng serve on http://localhost:4200 (the API is proxied)
dotnet test                 # scanner and server tests
cd src/web && npm test -- --watch=false     # UI tests (Vitest)
```

`npm start` in `src/web` with `?mock` (or `?mock=150`) in the URL runs the UI on sample data without the server. Tests and experiments must never touch your real data folder: set `Radar__DataDir` to a temporary directory.

[CLAUDE.md](CLAUDE.md) is the project guide for people and agents: architecture, conventions and the reasoning behind the rules. Read the parts about the area you change. The ones that matter most:

- **Everything the UI shows is in two languages.** Add a key to `src/web/src/app/i18n/pl.ts` and the same key to `en.ts`; a test checks that they match. No hard-coded text in templates.
- **The server builds every command and every written text.** The browser never supplies text to run or to write into a repository.
- **Scanning stays read-only and local.** No network calls at runtime, never read secrets, and every path must be resolved and checked to lie inside a detected repository.
- **A write into a repository needs a plan and the user's confirmation**, is all-or-nothing and never commits.
- **Cross-platform:** the code has to work on macOS and Windows (Linux except for opening a terminal). CI runs the tests on all three for pull requests.

## Pull requests

- Keep a pull request to one concern, with tests for what it changes (`dotnet test` and the UI tests must pass).
- Commit messages: a single sentence in the past tense, no body (for example `Fixed the scan overlay not closing after a cancelled scan`).
- Add a line to [CHANGELOG.md](CHANGELOG.md) under the next version when the change is visible to users.
- By contributing you agree that your work is released under the repository's [MIT licence](LICENSE).
