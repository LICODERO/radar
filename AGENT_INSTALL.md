# Installing R.A.D.A.R. (instructions for a coding agent)

> People: you do not need this file. See [INSTALL.md](INSTALL.md) for every way to install and run R.A.D.A.R.

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
`lookashdev/radar`, download `SHA256SUMS.txt`, **stop if the checksum differs**, unpack into `~/.radar/app`
(Windows: `%LOCALAPPDATA%\RADAR\app`), and link `~/.local/bin/radar` (macOS/Linux). They need no administrator rights.

macOS / Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/lookashdev/radar/main/install.sh -o /tmp/radar-install.sh
less /tmp/radar-install.sh   # or print it, so the user can see what it does
sh /tmp/radar-install.sh
```

Windows (PowerShell):

```powershell
irm https://raw.githubusercontent.com/lookashdev/radar/main/install.ps1 -OutFile $env:TEMP\radar-install.ps1
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

- How to start it: give the **full path**, `~/.local/bin/radar` (Windows: `%LOCALAPPDATA%\RADAR\app\radar.exe`). Do not tell the user to type
  plain `radar` unless you have checked their own shell, not yours: your session's `PATH` is often not the one their terminal has
  (`~/.local/bin` is usually missing on a fresh macOS). Check with `"$SHELL" -lic 'command -v radar'`; if it prints nothing, say plain `radar`
  will not work yet and offer the line `echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.zshrc` (`~/.bashrc` for bash). Do not run it yourself.
- The first start asks which folder holds their repositories. Scanning is read-only; the app writes into a repository only after it shows a
  plan and the user confirms.
- If you are Claude Code, you may **offer** the optional plugin that adds `/radar:start`, `/radar:update` and `/radar:uninstall`, and install it only
  when the user says yes (it changes their Claude Code settings): `claude plugin marketplace add lookashdev/radar`, then
  `claude plugin install radar@radar`, then `/reload-plugins`. Other agents skip this.
- Claude Code (`claude`) or Codex CLI on the PATH is optional: it is only needed for the buttons that draft files or open sessions.
- To update, run the same installer again. To remove it, see "Uninstalling" below.
- Help and bug reports: https://github.com/lookashdev/radar/issues

## Uninstalling (only when the user asks)

Never touch the user's repositories. Quit a running R.A.D.A.R. first (`pkill -x radar` on macOS/Linux, `Stop-Process -Name radar` on Windows) if
`http://127.0.0.1:5178/api/health` answers.

R.A.D.A.R. removes itself. Use the full path (see step 4 for why), first without `--confirm`, which only lists what would go:

```
~/.local/bin/radar uninstall                  # Windows: & "$env:LOCALAPPDATA\RADAR\app\radar.exe" uninstall
```

It lists the program folder, the `radar` link (Windows: the user PATH entry, if `-AddToPath` was used) and what it leaves alone. Show that to
the user. Then ask, separately, about the two optional parts and add the flags they agree to:

- `--data`: the app's own settings and last scan (the chosen scan folder and flow layout are forgotten).
- `--skill`: the `radar-second-brain` skill in Claude's skills folder, only when it is an untouched copy of ours.

Run the same command again with `--confirm` (and those flags) to delete. It never touches repositories, the second-brain vault or shell
profiles. Tell the user what remains so they can decide: the vault folder (their notes); what the app wrote into repositories after they
confirmed it (`.claude/` files it created, the `radar` blocks in `CLAUDE.local.md` / `CLAUDE.md` between `<!-- radar:... -->` markers, the block in
`.git/info/exclude`); and a `export PATH=...` line in `~/.zshrc`/`~/.bashrc` if one was added (remove it only if the user agrees). Offer to list the
repository files, but change them per repository, on request only.

If the installed copy has no `uninstall` command (versions before 0.1.2), do it by hand and say so: delete `~/.radar` and `~/.local/bin/radar`
(only if `readlink` shows it points into `~/.radar`), on Windows `%LOCALAPPDATA%\RADAR` and its entry in the user PATH; the settings folder
(`~/Library/Application Support/RADAR`, `~/.local/share/radar` or `%APPDATA%\RADAR`) only after asking.
