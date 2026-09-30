#!/usr/bin/env bash
# Builds the UI (when needed) and starts R.A.D.A.R on http://127.0.0.1:5178. Use --rebuild to force a UI rebuild.
set -euo pipefail
cd "$(dirname "$0")"

WWW=src/api/Radar.Server/wwwroot

if [[ "${1:-}" == "--rebuild" || ! -f "$WWW/index.html" ]]; then
  echo "==> Building the UI"
  (cd src/web && { [[ -d node_modules ]] || npm ci; } && npx ng build)
  rm -rf "$WWW" && mkdir -p "$WWW"
  cp -R src/web/dist/web/browser/. "$WWW/"
fi

echo "==> Starting R.A.D.A.R (Ctrl+C to stop)"
exec dotnet run --project src/api/Radar.Server -c Release
