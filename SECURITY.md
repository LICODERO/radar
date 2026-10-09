# Security

## How R.A.D.A.R. is built to be safe

- It listens on `127.0.0.1` only, checks the `Host` and `Origin` headers, and every API call needs a per-run session token.
- It makes no network calls while running and has no telemetry.
- It only reads markdown/AI files of the repositories it scans (never `.env` or keys) and refuses paths that leave a repository.
- It writes into a repository only after showing you the plan and getting your confirmation: a new agent/skill/workflow file, the blocks it marks with `radar:` comments in `CLAUDE.local.md`, `CLAUDE.md` or the flow files, and entries in `.git/info/exclude`. It never commits. `radar uninstall` (it lists first and deletes only with `--confirm`) removes only the program folder, the `radar` link or PATH entry the installer made and, when asked, the app's own settings and bundled skill; it refuses a folder that is not an installed copy or sits in a git repository.
- The commands it runs in a terminal are built by the server from fixed templates, never from text sent by the browser.

## Reporting a problem

Please do **not** open a public issue for a security problem. Use GitHub's private vulnerability reporting for this repository (the **Security** tab → **Report a vulnerability**). I will answer as soon as I can; this is a one-person project, so please allow a few days.
