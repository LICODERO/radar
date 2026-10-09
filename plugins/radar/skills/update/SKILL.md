---
name: update
description: Update R.A.D.A.R. to the latest release
---

Update R.A.D.A.R. for the user.

1. Quit a running copy first if `http://127.0.0.1:5178/api/health` answers (`pkill -x radar` on macOS/Linux, `Stop-Process -Name radar` on Windows).
2. Run the installer again exactly as in step 2 of https://raw.githubusercontent.com/lookashdev/radar/main/AGENT_INSTALL.md (fetch the script,
   show it to the user, run it). It verifies the checksum and replaces the previous copy; the user's settings are kept.
3. Tell the user what the installer printed (version and folder). Do not start the app unless they ask (`/radar:start`).
