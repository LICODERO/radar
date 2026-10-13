---
description: Install R.A.D.A.R. (the app and its Claude Code row); afterwards use /radar for everything else
allowed-tools: Bash
---

Install R.A.D.A.R. for the user. Answer in the user's language. Do not use sudo or administrator rights, do not edit shell profiles or the
registry, and do not install anything else.

1. Find out the system: `uname -s` (macOS/Linux) or, when that does not exist, assume Windows (PowerShell).
2. If R.A.D.A.R. is already installed (`~/.radar/app/radar`, Windows: `%LOCALAPPDATA%\RADAR\app\radar.exe`), say so and point to `/radar update`; stop.
3. Fetch the installer of this repository to a temporary file, show its first lines to the user, then run it. It checks the SHA256 of the
   archive itself and stops on a mismatch; if it fails, report the error and stop (never try another source).
   - macOS/Linux: `curl -fsSL https://raw.githubusercontent.com/LICODERO/radar/main/install.sh -o "$TMPDIR/radar-install.sh" && sh "$TMPDIR/radar-install.sh"` (use `/tmp` when `TMPDIR` is empty)
   - Windows: `Invoke-WebRequest -UseBasicParsing https://raw.githubusercontent.com/LICODERO/radar/main/install.ps1 -OutFile $env:TEMP\radar-install.ps1; powershell -NoProfile -ExecutionPolicy Bypass -File $env:TEMP\radar-install.ps1`
4. Check that the installer printed `Checksum OK` and that the program exists at the path above.
5. Tell the user, briefly: the row with the version and the start/stop buttons shows above the prompt after `/reload-plugins` (or in a new
   session); `/radar start` runs the app, `/radar` lists everything (status, restart, open, hide, show, update, uninstall). Do not start the
   app yourself unless the user asks.

$ARGUMENTS
