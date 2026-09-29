# Windows equivalent of run.sh: builds the UI (when needed) and starts R.A.D.A.R on http://127.0.0.1:5178.
# Use -Rebuild to force a UI rebuild.
param([switch]$Rebuild)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$www = 'src/Radar.Server/wwwroot'
if ($Rebuild -or -not (Test-Path "$www/index.html")) {
    Write-Host '==> Building the UI'
    Push-Location web
    if (-not (Test-Path node_modules)) { npm ci }
    npx ng build
    Pop-Location
    if (Test-Path $www) { Remove-Item $www -Recurse -Force }
    New-Item -ItemType Directory -Path $www | Out-Null
    Copy-Item 'web/dist/web/browser/*' $www -Recurse
}

Write-Host '==> Starting R.A.D.A.R (Ctrl+C to stop)'
dotnet run --project src/Radar.Server -c Release
