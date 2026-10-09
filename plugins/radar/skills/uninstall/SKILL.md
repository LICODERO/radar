---
name: uninstall
description: Remove R.A.D.A.R. from this computer (lists what goes first, asks before deleting)
disable-model-invocation: true
---

Remove R.A.D.A.R. for the user, following the section "Uninstalling" of
https://raw.githubusercontent.com/lookashdev/radar/main/AGENT_INSTALL.md. In short:

1. Quit a running copy if `http://127.0.0.1:5178/api/health` answers.
2. Run `~/.local/bin/radar uninstall` (Windows: `& "$env:LOCALAPPDATA\RADAR\app\radar.exe" uninstall`) **without** `--confirm` and show the
   user its list. If the command does not exist (versions before 0.1.2), use the manual steps from the guide and say so.
3. Ask separately whether to also remove the settings (`--data`) and the `radar-second-brain` skill (`--skill`).
4. Only after the user agrees, run it again with `--confirm` and the flags they chose.
5. Say what stays: their repositories (including what R.A.D.A.R. wrote there after they confirmed it), the second-brain vault and any PATH line in
   their shell profile. Never delete those yourself.
