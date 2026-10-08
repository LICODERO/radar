# R.A.D.A.R.

**Repo AI Discovery And Review** – a local dashboard that scans a folder of repositories and shows how well each one is set up for AI coding agents (`CLAUDE.md`, agents, skills, workflows), where the gaps are, and gives you the commands to fill them with Claude Code or Codex CLI.

🌐 **Website:** https://radar.licodero.pl · 🐞 **Bugs and ideas:** [open an issue](../../issues) · 🇵🇱 [Po polsku niżej](#po-polsku)

Everything runs on your machine. Nothing is sent anywhere, and R.A.D.A.R. only writes into a repository after you have seen what it will write and confirmed it.

## Download

| System | File |
|---|---|
| macOS, Apple Silicon (M1 and newer) | [radar-osx-arm64.zip](../../releases/latest/download/radar-osx-arm64.zip) |
| macOS, Intel | [radar-osx-x64.zip](../../releases/latest/download/radar-osx-x64.zip) |
| Windows 10/11 (64-bit) | [radar-win-x64.zip](../../releases/latest/download/radar-win-x64.zip) |
| Linux (64-bit) | [radar-linux-x64.tar.gz](../../releases/latest/download/radar-linux-x64.tar.gz) |

Checksums: [SHA256SUMS.txt](../../releases/latest/download/SHA256SUMS.txt) · All versions and release notes: [Releases](../../releases).

There is nothing to install: unpack the archive and start `radar` (`radar.exe` on Windows). It opens `http://127.0.0.1:5178` in your browser. Close the terminal window to quit.

### macOS

The app is not signed with an Apple Developer ID, so Gatekeeper blocks the first start. Remove the download flag once:

```bash
cd ~/Downloads/radar          # wherever you unpacked it
xattr -dr com.apple.quarantine .
./radar
```

### Windows

SmartScreen may show "Windows protected your PC" because the file is not signed. Choose **More info → Run anyway**.

### Linux

```bash
tar -xzf radar-linux-x64.tar.gz && cd radar && ./radar
```

(Opening a terminal for you works on macOS and Windows; on Linux copy the generated commands instead.)

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

**Pobieranie:** tabela w sekcji [Download](#download). Nic nie trzeba instalować: rozpakuj i uruchom `radar` (`radar.exe` w Windows), otworzy się `http://127.0.0.1:5178`.

- **macOS:** aplikacja nie jest podpisana, więc przy pierwszym starcie wykonaj `xattr -dr com.apple.quarantine .` w rozpakowanym folderze.
- **Windows:** SmartScreen może ostrzec; wybierz **Więcej informacji → Uruchom mimo to**.
- **Zgłoszenia błędów i pomysły:** [Issues](../../issues), po polsku lub angielsku.
