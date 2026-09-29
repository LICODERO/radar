#!/usr/bin/env bash
# Development mode: .NET server with hot reload + Angular dev server (http://localhost:4200, proxied to the API).
set -euo pipefail
cd "$(dirname "$0")"

[[ -d web/node_modules ]] || (cd web && npm ci)

export Radar__AllowedOrigins__0="http://localhost:4200"
export Radar__OpenBrowser=false

(cd web && npm start) &
WEB_PID=$!
trap 'kill $WEB_PID 2>/dev/null || true' EXIT INT TERM

dotnet watch run --project src/Radar.Server
