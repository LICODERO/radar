# R.A.D.A.R.

**Repo AI Discovery And Review** – a local dashboard that scans a folder of repositories and shows how well each one is set up for AI coding agents (`CLAUDE.md`, agents, skills, workflows), where the gaps are, and gives you the commands to fill them with Claude Code or Codex CLI.

🌐 **Website:** https://radar.licodero.pl · 🐞 **Bugs and ideas:** [open an issue](../../issues) · 🇵🇱 [Po polsku niżej](#po-polsku)

Everything runs on your machine. Nothing is sent anywhere, and R.A.D.A.R. only writes into a repository after you have seen what it will write and confirmed it.

## Install

**With your coding agent.** Paste this into Claude Code, Codex or any agent that works in a terminal:

```text
Read https://raw.githubusercontent.com/lookashdev/radar-releases/main/AGENT_INSTALL.md and set up R.A.D.A.R. for me: run the steps, verify the checksum, and tell me how to start it.
```

**Or with one command.**

```bash
# macOS / Linux
curl -fsSL https://raw.githubusercontent.com/lookashdev/radar-releases/main/install.sh | sh
```

```powershell
# Windows (PowerShell)
irm https://raw.githubusercontent.com/lookashdev/radar-releases/main/install.ps1 | iex
```

Both scripts ([install.sh](install.sh), [install.ps1](install.ps1)) are short, so read them first if you like. They download the archive for your system from the latest release, **stop if the SHA256 checksum does not match**, unpack it into `~/.radar/app` (Windows: `%LOCALAPPDATA%\RADAR\app`) and need no administrator rights. Run the same command again to update; delete that folder to remove R.A.D.A.R. A file fetched from a terminal is not marked "downloaded from the internet", so macOS and Windows usually show no unknown-app warning.

Start it with `radar` (or the path the installer prints). It opens `http://127.0.0.1:5178` in your browser. Close the terminal window to quit.

### Download by hand

| System | File |
|---|---|
| macOS, Apple Silicon (M1 and newer) | [radar-osx-arm64.zip](../../releases/latest/download/radar-osx-arm64.zip) |
| macOS, Intel | [radar-osx-x64.zip](../../releases/latest/download/radar-osx-x64.zip) |
| Windows 10/11 (64-bit) | [radar-win-x64.zip](../../releases/latest/download/radar-win-x64.zip) |
| Linux (64-bit) | [radar-linux-x64.tar.gz](../../releases/latest/download/radar-linux-x64.tar.gz) |

Checksums: [SHA256SUMS.txt](../../releases/latest/download/SHA256SUMS.txt) · All versions and release notes: [Releases](../../releases). Unpack the archive and start `radar` (`radar.exe` on Windows).

A browser download carries the "from the internet" mark, and the app is not signed, so:

- **macOS:** Gatekeeper blocks the first start. Remove the mark once:

  ```bash
  cd ~/Downloads/radar          # wherever you unpacked it
  xattr -dr com.apple.quarantine .
  ./radar
  ```

- **Windows:** SmartScreen may show "Windows protected your PC". Choose **More info → Run anyway**.
- **Linux:** `tar -xzf radar-linux-x64.tar.gz && cd radar && ./radar`. Opening a terminal for you works on macOS and Windows; on Linux copy the generated commands instead.

## Requirements

- A folder with your Git repositories.
- [Claude Code](https://claude.com/claude-code) and/or Codex CLI on your `PATH` if you want R.A.D.A.R. to draft agents or open sessions for you. The dashboard itself works without them.

## Check the download

```bash
shasum -a 256 -c SHA256SUMS.txt --ignore-missing      # macOS / Linux
```

## What it does

- **Orbit view** of repositories, agents, skills and workflows with coverage and quality scores.
- **Gaps** with ready-to-run commands for Claude Code and Codex CLI.
- **Drafts** for agents, skills and workflows; copies shared ones between repos; keeps private ones out of git.
- **Second brain**: a vault of markdown notes outside your repositories.
- **Flow between repos**: draw which repo calls which, how it authenticates and which way the data goes. The rules are written to both repos and pointed to from `CLAUDE.md`, so an agent working in one repo knows to look at the other and ask first.

## Feedback

This repository holds the releases and the issue tracker. Use [Issues](../../issues) for bugs and feature requests (the templates ask for your system and version). Security problems: see [SECURITY.md](SECURITY.md).

## Licence

Free to use, see [LICENSE](LICENSE). Third-party components: see `THIRD-PARTY-NOTICES.md` inside the download.

---

## Po polsku

**R.A.D.A.R.** (Repo AI Discovery And Review) to lokalny dashboard, który skanuje folder z repozytoriami i pokazuje, jak dobrze każde z nich jest przygotowane pod agentów AI (`CLAUDE.md`, agenci, skille, workflow), gdzie są luki, i podaje komendy do ich uzupełnienia w Claude Code lub Codex CLI.

Wszystko działa na Twoim komputerze, nic nie jest nigdzie wysyłane, a zapis do repozytorium następuje dopiero po pokazaniu, co zostanie zapisane, i Twoim potwierdzeniu.

**Instalacja:** wklej agentowi (Claude Code, Codex...) zdanie z sekcji [Install](#install) albo uruchom jedną komendę (`install.sh` na macOS i Linuksie, `install.ps1` w Windows). Skrypty sprawdzają sumę SHA256 i nie wymagają uprawnień administratora. Ręczne pobranie jest w sekcji „Download by hand”. Po starcie otwiera się `http://127.0.0.1:5178`.

- **macOS (po ręcznym pobraniu w przeglądarce):** aplikacja nie jest podpisana, więc przy pierwszym starcie wykonaj `xattr -dr com.apple.quarantine .` w rozpakowanym folderze.
- **Windows (po ręcznym pobraniu):** SmartScreen może ostrzec; wybierz **Więcej informacji → Uruchom mimo to**.
- **Zgłoszenia błędów i pomysły:** [Issues](../../issues), po polsku lub angielsku.
