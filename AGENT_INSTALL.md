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
- Claude Code (`claude`) or Codex CLI on the PATH is optional: it is only needed for the buttons that draft files or open sessions.
- To update, run the same installer again. To remove it, see "Uninstalling" below.
- Help and bug reports: https://github.com/lookashdev/radar/issues

## Uninstalling (only when the user asks)

Tell the user what you are about to delete, then do exactly this and nothing more. Never touch their repositories.

1. Stop a running R.A.D.A.R. (`pkill -x radar` on macOS/Linux, `Stop-Process -Name radar` on Windows) if `http://127.0.0.1:5178/api/health` answers.
2. Delete the app and the link:
   - macOS/Linux: `rm -rf ~/.radar` and `rm -f ~/.local/bin/radar` (only if it is a symlink into `~/.radar`: check with `readlink`).
   - Windows: delete `%LOCALAPPDATA%\RADAR`.
3. Delete the app's own settings and last scan (`settings.json`, `scan-result.json`): the folder `~/Library/Application Support/RADAR` (macOS),
   `~/.local/share/radar` (Linux) or `%APPDATA%\RADAR` (Windows). Ask first: this forgets the chosen scan folder and relations layout.
4. Remove what you or the user added to the shell: the `export PATH=...` line in `~/.zshrc`/`~/.bashrc` **only if it was added for R.A.D.A.R. and the
   user agrees**; on Windows, the `%LOCALAPPDATA%\RADAR\app` entry in the user PATH if `-AddToPath` was used.
5. Optional, ask separately: the `radar-second-brain` skill in `~/.claude/skills/radar-second-brain` (or `$CLAUDE_CONFIG_DIR/skills/...`), only
   when its `SKILL.md` has the R.A.D.A.R. marker line.

Leave these alone and tell the user they remain, so they can decide: the second-brain vault folder (their notes), and what the app wrote into
repositories after they confirmed it: `.claude/` files it created, the `radar` blocks in `CLAUDE.local.md` / `CLAUDE.md` (between `<!-- radar:... -->`
markers) and in `.git/info/exclude` (between `# >>> radar:private >>>` markers). Offer to list them, but delete them only per repository on request.
