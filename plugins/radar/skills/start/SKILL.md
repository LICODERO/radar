---
name: start
description: Start R.A.D.A.R. and give the user the dashboard address
---

Start R.A.D.A.R. for the user.

1. If `http://127.0.0.1:5178/api/health` already answers `{"status":"ok"...}`, it is running: give the user `http://127.0.0.1:5178` and stop.
2. Otherwise start the installed program in the background by its full path: `~/.local/bin/radar` (macOS/Linux) or
   `%LOCALAPPDATA%\RADAR\app\radar.exe` (Windows). Never rely on plain `radar` being on the PATH.
3. If that file does not exist, R.A.D.A.R. is not installed: tell the user and suggest `/radar:install`.
4. Wait until the health address answers, then give the user `http://127.0.0.1:5178` (it also opens in their browser). Tell them how to stop it:
   end the background task, or Ctrl+C when it runs in a terminal.
