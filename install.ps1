<#
.SYNOPSIS
  R.A.D.A.R. installer for Windows.

.DESCRIPTION
  irm https://raw.githubusercontent.com/lookashdev/radar/main/install.ps1 | iex

  What it does, and nothing else:
    1. downloads radar-win-x64.zip from the latest release (or $env:RADAR_VERSION = '0.1.0') and SHA256SUMS.txt,
       and stops when the checksum does not match,
    2. unpacks it into %LOCALAPPDATA%\RADAR\app (replacing a previous copy).
  It needs no administrator rights and changes neither the registry nor PATH. To put "radar" on your PATH, run with -AddToPath
  (download the script first: irm <url> -OutFile install.ps1; .\install.ps1 -AddToPath). To remove R.A.D.A.R., delete %LOCALAPPDATA%\RADAR.

  Environment (all optional): RADAR_VERSION, RADAR_HOME, RADAR_REPO (default lookashdev/radar),
  RADAR_BASE_URL (a mirror or a test server; replaces the GitHub download URL).
#>
param([switch]$AddToPath)

& {
  param([bool]$AddToPath)
  $ErrorActionPreference = 'Stop'
  $ProgressPreference = 'SilentlyContinue'      # Invoke-WebRequest is very slow with the progress bar
  [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

  function Fail($m) { throw "R.A.D.A.R. installer: $m" }

  $repo = if ($env:RADAR_REPO) { $env:RADAR_REPO } else { 'lookashdev/radar' }
  $root = if ($env:RADAR_HOME) { $env:RADAR_HOME } else { Join-Path $env:LOCALAPPDATA 'RADAR' }
  $file = 'radar-win-x64.zip'

  if ($env:RADAR_BASE_URL) { $base = $env:RADAR_BASE_URL }
  elseif ($env:RADAR_VERSION) { $base = "https://github.com/$repo/releases/download/v$($env:RADAR_VERSION.TrimStart('v'))" }
  else { $base = "https://github.com/$repo/releases/latest/download" }

  $tmp = Join-Path ([IO.Path]::GetTempPath()) ('radar-' + [Guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Path $tmp | Out-Null
  try {
    Write-Host "Downloading $file ..."
    Invoke-WebRequest -UseBasicParsing -Uri "$base/$file" -OutFile (Join-Path $tmp $file)
    Invoke-WebRequest -UseBasicParsing -Uri "$base/SHA256SUMS.txt" -OutFile (Join-Path $tmp 'SHA256SUMS.txt')

    $line = Get-Content (Join-Path $tmp 'SHA256SUMS.txt') | Where-Object { ($_ -split '\s+', 2)[1] -replace '^\*', '' -eq $file } | Select-Object -First 1
    if (-not $line) { Fail "$file is not listed in SHA256SUMS.txt" }
    $want = ($line -split '\s+')[0].ToLowerInvariant()
    $got = (Get-FileHash -Algorithm SHA256 (Join-Path $tmp $file)).Hash.ToLowerInvariant()
    if ($want -ne $got) { Fail "checksum mismatch for $file (expected $want, got $got); nothing was installed" }
    Write-Host 'Checksum OK.'

    $x = Join-Path $tmp 'x'
    Expand-Archive -Path (Join-Path $tmp $file) -DestinationPath $x
    $exe = Join-Path $x 'radar\radar.exe'
    if (-not (Test-Path $exe)) { Fail 'the archive does not contain radar\radar.exe' }

    New-Item -ItemType Directory -Force -Path $root | Out-Null
    $app = Join-Path $root 'app'
    if (Test-Path $app) { Remove-Item -Recurse -Force $app }
    Move-Item (Join-Path $x 'radar') $app
    Get-ChildItem -Recurse $app | Unblock-File -ErrorAction SilentlyContinue

    if ($AddToPath) {
      $user = [Environment]::GetEnvironmentVariable('Path', 'User')
      if (($user -split ';') -notcontains $app) {
        [Environment]::SetEnvironmentVariable('Path', ($user.TrimEnd(';') + ';' + $app), 'User')
        Write-Host "Added $app to your user PATH (open a new terminal for it to take effect)."
      }
    }

    Write-Host ''
    Write-Host "Installed in $app"
    Write-Host "Start it with:  $app\radar.exe"
    if (-not $AddToPath) { Write-Host 'To start it with just "radar", run this script again with -AddToPath.' }
    Write-Host 'It opens http://127.0.0.1:5178 in your browser. Close the console window to quit.'
  }
  finally { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }
} -AddToPath:$AddToPath
