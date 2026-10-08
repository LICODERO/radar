# Installing R.A.D.A.R. (instructions for a coding agent)

You are asked to install R.A.D.A.R. for the user on their machine. R.A.D.A.R. is a local dashboard that shows how well a folder of git
repositories is set up for AI coding agents. Follow these steps in order, keep the user informed, and stop at the first problem.

## Ground rules

- Do **not** use `sudo`, do not edit shell profiles or the registry, and do not install anything else (no .NET, no Node).
- The only commands you run are the installer script from this repository and the checks below. Read the script before you run it.
- If a checksum fails or a download fails, stop and tell the user. Do not try another source.

## 1. Find out the system

Run `uname -s && uname -m` (macOS/Linux) or check `$env:OS` / `$env:PROCESSOR_ARCHITECTURE` (Windows).

| System | Installer |
|---|---|
| macOS (Apple Silicon or Intel) or Linux x86-64 | `install.sh` |
| Windows 10/11 (64-bit) | `install.ps1` |

Linux on ARM is not supported; say so and stop.

## 2. Read the installer, then run it

The scripts are small and do exactly this: download the archive for this system from the latest GitHub release of
`lookashdev/radar-releases`, download `SHA256SUMS.txt`, **stop if the checksum differs**, unpack into `~/.radar/app`
(Windows: `%LOCALAPPDATA%\RADAR\app`), and link `~/.local/bin/radar` (macOS/Linux). They need no administrator rights.

macOS / Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/lookashdev/radar-releases/main/install.sh -o /tmp/radar-install.sh
less /tmp/radar-install.sh   # or print it, so the user can see what it does
sh /tmp/radar-install.sh
```

Windows (PowerShell):

```powershell
irm https://raw.githubusercontent.com/lookashdev/radar-releases/main/install.ps1 -OutFile $env:TEMP\radar-install.ps1
Get-Content $env:TEMP\radar-install.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File $env:TEMP\radar-install.ps1
```

Pass `-AddToPath` to the Windows script only if the user asks for `radar` to be on their PATH.

## 3. Verify

- The script printed `Checksum OK.` and the install folder.
- The executable exists: `~/.radar/app/radar` (Windows: `%LOCALAPPDATA%\RADAR\app\radar.exe`).

Do not start the app unless the user asks. If they do, run it in a separate terminal or in the background
(`~/.radar/app/radar`), wait until `http://127.0.0.1:5178/api/health` answers `{"status":"ok"...}` and tell the user the dashboard is at
`http://127.0.0.1:5178` (it also opens in their browser).

## 4. Tell the user

- How to start it: `~/.local/bin/radar` (or just `radar` if `~/.local/bin` is on their PATH; otherwise offer the `export PATH=...` line the
  installer printed, but do not add it yourself). On Windows: `%LOCALAPPDATA%\RADAR\app\radar.exe`.
- The first start asks which folder holds their repositories. Scanning is read-only; the app writes into a repository only after it shows a
  plan and the user confirms.
- Claude Code (`claude`) or Codex CLI on the PATH is optional: it is only needed for the buttons that draft files or open sessions.
- To update, run the same installer again. To remove: delete `~/.radar` and `~/.local/bin/radar` (Windows: `%LOCALAPPDATA%\RADAR`); the app's
  settings live in `~/Library/Application Support/RADAR` (macOS), `~/.local/share/radar` (Linux) or `%APPDATA%\RADAR` (Windows).
- Help and bug reports: https://github.com/lookashdev/radar-releases/issues
