# Installing and running R.A.D.A.R.

Pick whichever fits you. All of them end with the same app: a local dashboard on `http://127.0.0.1:5178`.

| | Needs | Best for |
|---|---|---|
| [1. Let your coding agent do it](#1-let-your-coding-agent-do-it) | Claude Code, Codex or another terminal agent | the least typing |
| [2. One command](#2-one-command) | `curl` (macOS/Linux) or PowerShell (Windows) | a quick install without an agent |
| [3. Download by hand](#3-download-by-hand) | a browser | when you prefer to see every step |
| [4. Run from source](#4-run-from-source) | .NET SDK 10 and Node.js 22 | contributors, or when you want to read and build it yourself |

Nothing needs administrator rights, and nothing is installed outside the folders named below.

## 1. Let your coding agent do it

Paste this into Claude Code, Codex or any agent that works in a terminal:

```text
Read https://raw.githubusercontent.com/LICODERO/radar/main/AGENT_INSTALL.md and set up R.A.D.A.R. for me: run the steps, verify the checksum, and tell me how to start it.
```

[AGENT_INSTALL.md](AGENT_INSTALL.md) tells the agent to read the installer, run it, verify the result and report back. It forbids `sudo`, edits to your shell profile and installing anything else. The agent asks for your approval for each command in the usual way, so you see what it does.

## 2. One command

macOS and Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/LICODERO/radar/main/install.sh | sh
```

Windows (PowerShell):

```powershell
irm https://raw.githubusercontent.com/LICODERO/radar/main/install.ps1 | iex
```

If you would rather read a script before you run it (a good habit), open [install.sh](install.sh) or [install.ps1](install.ps1). They do exactly this:

1. choose the archive for your system from the latest [release](https://github.com/LICODERO/radar/releases/latest),
2. download it and `SHA256SUMS.txt`, and **stop if the checksum does not match** (nothing is installed then),
3. unpack it into `~/.radar/app` (Windows: `%LOCALAPPDATA%\RADAR\app`) and, on macOS and Linux, link it as `~/.local/bin/radar`.

Options (environment variables): `RADAR_VERSION` (for example `0.1.0`, default: the latest), `RADAR_HOME` (install folder), `RADAR_BIN_DIR` (where the `radar` link goes), `RADAR_BASE_URL` (a mirror). On Windows, `-AddToPath` puts `radar` on your user PATH; download the script first (`irm <url> -OutFile install.ps1`) to pass it.

A file fetched from a terminal does not carry the "downloaded from the internet" mark, which is why macOS and Windows usually show no unknown-app warning for it.

## 3. Download by hand

Take the archive for your system from the [latest release](https://github.com/LICODERO/radar/releases/latest), unpack it and start `radar` (`radar.exe` on Windows):

| System | File |
|---|---|
| macOS, Apple Silicon (M1 and newer) | `radar-osx-arm64.zip` |
| macOS, Intel | `radar-osx-x64.zip` |
| Windows 10/11, 64-bit | `radar-win-x64.zip` |
| Linux, x86-64 | `radar-linux-x64.tar.gz` |

To check the download: `shasum -a 256 -c SHA256SUMS.txt --ignore-missing` (macOS/Linux) or `Get-FileHash radar-win-x64.zip` and compare with the line in `SHA256SUMS.txt` (Windows).

The app is not code-signed (no paid certificates), so a **browser** download triggers the system's warning:

- **macOS:** Gatekeeper blocks the first start. Remove the mark once, in the unpacked folder:

  ```bash
  xattr -dr com.apple.quarantine .
  ./radar
  ```

- **Windows:** SmartScreen may show "Windows protected your PC". Choose **More info → Run anyway**. On Windows 11 with Smart App Control switched on, unsigned apps can be blocked outright; use option 4 or turn that setting off if you trust the download.
- **Linux:** `tar -xzf radar-linux-x64.tar.gz && cd radar && ./radar`.

## 4. Run from source

You need the [.NET SDK 10](https://dotnet.microsoft.com/download) and [Node.js 22](https://nodejs.org). Then:

```bash
git clone https://github.com/LICODERO/radar.git
cd radar
./run.sh            # Windows: ./run.ps1
```

`run.sh` builds the UI on the first run (`./run.sh --rebuild` to build it again), starts the server on `http://127.0.0.1:5178` and opens your browser. To work on it, see [CONTRIBUTING.md](CONTRIBUTING.md).

## Starting, updating, removing

- **Start:** `radar` (if `~/.local/bin` is on your PATH), or the full path the installer printed. The app opens your browser; close the terminal window (or press Ctrl+C) to quit. It needs Claude Code (`claude`) or Codex CLI on your PATH only for the buttons that draft files or open sessions.
- **Update:** run the installer again. A manual install: replace the folder with the new archive.
- **Remove:** run `radar uninstall` (it lists what it would delete; add `--confirm` to do it, `--data` to also forget the settings, `--skill` for the bundled skill). Versions before 0.1.2 do not have it, and by hand it is: delete `~/.radar` and `~/.local/bin/radar` (Windows: `%LOCALAPPDATA%\RADAR`). The app keeps its settings and the last scan in `~/Library/Application Support/RADAR` (macOS), `~/.local/share/radar` (Linux) or `%APPDATA%\RADAR` (Windows); delete that folder to forget them.

## Claude Code plugin (optional)

If you use Claude Code, a small plugin gives you slash commands for the day-to-day: `/radar:install`, `/radar:start`, `/radar:update` and
`/radar:uninstall`. It does not contain the app; the commands run the installer and `radar uninstall` described here.

```bash
claude plugin marketplace add LICODERO/radar
claude plugin install radar@radar
```

Then run `/reload-plugins` (or start a new session) and `/radar:install`. Adding a marketplace and a plugin changes your Claude Code settings, so
this is always your choice. To remove it: `claude plugin uninstall radar@radar`, which does not remove the app (`/radar:uninstall` does).

## Troubleshooting

- **"Address already in use":** something else uses port 5178. Start with another one: `Radar__Url=http://127.0.0.1:5179 radar` (Windows PowerShell: `$env:Radar__Url='http://127.0.0.1:5179'; radar`).
- **The browser does not open:** open `http://127.0.0.1:5178` yourself. Set `Radar__OpenBrowser=false` to stop it from trying.
- **`radar: command not found`:** `~/.local/bin` is not on your PATH. Add `export PATH="$HOME/.local/bin:$PATH"` to your shell profile, or run `~/.radar/app/radar`.
- **Anything else:** [open an issue](https://github.com/LICODERO/radar/issues/new/choose) with your system, the version (bottom-right corner of the app) and the text from the terminal window.
