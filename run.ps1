# ServiceHub 4.2.1 - run from source on Windows (PowerShell or Command Prompt).
#
# One command:   .\run.cmd        (works in Command Prompt AND PowerShell; `run` alone also works in cmd)
#      or:       powershell -ExecutionPolicy Bypass -File .\run.ps1
#
# It checks the machine, downloads and installs what is missing (the .NET 10 SDK and Node.js - into
# %USERPROFILE%\.servicehub\tools, no admin rights, nothing system-wide is touched), restores and builds
# everything, starts the API and the web server together, tells you when both are answering, and stops both
# (and everything they started) on Ctrl-C. Mirrors run.sh (macOS / Linux) option for option.
#
# NOTE: keep this file ASCII-only. Windows PowerShell 5.1 reads BOM-less files as ANSI and would garble anything else.
[CmdletBinding()]
param(
  [switch]$ApiOnly,
  [switch]$WebOnly,
  [switch]$Check,
  [switch]$Docker,
  [switch]$Local,
  [switch]$NoInstall,
  [switch]$Yes,
  [Alias('h')][switch]$Help
)

$ErrorActionPreference = 'Continue'   # native commands are judged by $LASTEXITCODE, never by stderr noise
$ProgressPreference = 'SilentlyContinue'   # the progress bar makes Invoke-WebRequest 10x slower on 5.1
try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch {}

$ROOT = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ROOT

function Show-Usage {
@'
Usage: run.cmd [-Local | -Docker | -ApiOnly | -WebOnly | -Check] [-Yes | -NoInstall]

  (no flags)   Check the machine, install anything missing, then start the API and the web dev server.
  -ApiOnly     Start only the .NET API.
  -WebOnly     Start only the Vite dev server (expects an API already running).
  -Local       Run from source on this machine without asking "local or Docker?".
  -Docker      Run in Docker instead (checks Docker, the daemon and Compose, makes and keeps an encryption key,
               then builds and starts the container on http://localhost:8080). Add -Check to only verify.
  -Check       Report what is ready and what would be installed, then exit. Starts and installs nothing.
  -Yes         Download missing tools without asking (default: you are asked first).
  -NoInstall   Never download anything; stop with instructions if a tool is missing.

Environment (set with  set NAME=value  in cmd, or  $env:NAME = 'value'  in PowerShell):
  SERVICEHUB_RUN            local | docker - answers the "local or Docker?" question in advance
  SERVICEHUB_API_PORT       API port (default 5153; if unset and busy, the next free port is used)
  SERVICEHUB_WEB_PORT       Dev server port (default 3000; same rule)
  SERVICEHUB_DATA_DIR       Where the SQLite database lives (default: services\api\src\ServiceHub.Api\data)
  SERVICEHUB_READY_TIMEOUT  Seconds to wait for startup before giving up (default 180)
  SERVICEHUB_AUTO_INSTALL   1 = same as -Yes, 0 = same as -NoInstall (default: ask before downloading)
  SERVICEHUB_TOOLS_DIR      Where a missing .NET SDK / Node.js is installed (default %USERPROFILE%\.servicehub\tools)
  ASPNETCORE_ENVIRONMENT    Defaults to Development, which supplies a throw-away dev encryption key
'@
}
if ($Help) { Show-Usage; exit 0 }

# --- Output helpers -----------------------------------------------------------------------------
function Die([string]$msg) { Write-Host "x $msg" -ForegroundColor Red; exit 1 }
function Warn([string]$msg) { Write-Host "! $msg" -ForegroundColor Yellow }
function Step([string]$msg) { Write-Host "> $msg" -ForegroundColor Cyan }
function Ok([string]$msg) { Write-Host "+ $msg" -ForegroundColor Green }
function Have([string]$name) { [bool](Get-Command $name -ErrorAction SilentlyContinue) }

# --- Settings -----------------------------------------------------------------------------------
function Get-Port([string]$var, [int]$default) {
  $raw = [Environment]::GetEnvironmentVariable($var)
  if ([string]::IsNullOrEmpty($raw)) { return @{ Port = $default; Fixed = $false } }
  $n = 0
  if (-not [int]::TryParse($raw, [ref]$n) -or $n -lt 1 -or $n -gt 65535) { Die "$var='$raw' is not a valid port (use a number from 1 to 65535)." }
  return @{ Port = $n; Fixed = $true }
}
$apiCfg = Get-Port 'SERVICEHUB_API_PORT' 5153; $API_PORT = $apiCfg.Port; $API_FIXED = $apiCfg.Fixed
$webCfg = Get-Port 'SERVICEHUB_WEB_PORT' 3000; $WEB_PORT = $webCfg.Port; $WEB_FIXED = $webCfg.Fixed
$READY_TIMEOUT = 180
if ($env:SERVICEHUB_READY_TIMEOUT -match '^\d+$') { $READY_TIMEOUT = [int]$env:SERVICEHUB_READY_TIMEOUT }
$AUTO_INSTALL = 'ask'   # ask (default) | yes | never
if ($Yes -or $env:SERVICEHUB_AUTO_INSTALL -eq '1') { $AUTO_INSTALL = 'yes' }
if ($NoInstall -or $env:SERVICEHUB_AUTO_INSTALL -eq '0') { $AUTO_INSTALL = 'never' }
$userHome = $env:USERPROFILE; if (-not $userHome) { $userHome = $HOME }; if (-not $userHome) { $userHome = $ROOT }
$TOOLS_DIR = $env:SERVICEHUB_TOOLS_DIR; if (-not $TOOLS_DIR) { $TOOLS_DIR = Join-Path $userHome '.servicehub\tools' }
$NODE_LTS_LINE = 'v22.x'

$MODE = 'both'
if ($ApiOnly) { $MODE = 'api' } elseif ($WebOnly) { $MODE = 'web' } elseif ($Check) { $MODE = 'check' }

# --- Platform -----------------------------------------------------------------------------------
$onWindows = ($PSVersionTable.PSVersion.Major -lt 6) -or ($IsWindows -eq $true)
if (-not $onWindows) { Die 'run.ps1 is for Windows. On macOS / Linux use ./run.sh' }
$archRaw = $env:PROCESSOR_ARCHITEW6432; if (-not $archRaw) { $archRaw = $env:PROCESSOR_ARCHITECTURE }
switch -Regex ($archRaw) {
  '^(AMD64|x86_64)$' { $ARCH = 'x64' }
  '^ARM64$'          { $ARCH = 'arm64' }
  default            { $ARCH = 'unsupported' }
}

# --- Process helpers ----------------------------------------------------------------------------
$script:Started = @()
$script:Scratch = $null

function Stop-Tree([int]$procId) { & taskkill.exe /PID $procId /T /F *> $null }

function Cleanup {
  if ($script:Scratch -and (Test-Path $script:Scratch)) { Remove-Item -Recurse -Force $script:Scratch -ErrorAction SilentlyContinue }
  $alive = @($script:Started | Where-Object { $_ -and -not $_.HasExited })
  if ($alive.Count -eq 0) { return }
  Write-Host ''; Write-Host '[] Stopping ServiceHub...'
  # Ctrl-C already reached the children (same console); give them up to 5 s to flush SQLite and exit by themselves.
  for ($i = 0; $i -lt 50; $i++) {
    if (-not ($script:Started | Where-Object { $_ -and -not $_.HasExited })) { return }
    Start-Sleep -Milliseconds 100
  }
  foreach ($p in $script:Started) { if ($p -and -not $p.HasExited) { Stop-Tree $p.Id } }
}

function New-Scratch {
  if (-not $script:Scratch) {
    $script:Scratch = Join-Path ([IO.Path]::GetTempPath()) ("servicehub-run-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force -Path $script:Scratch | Out-Null
  }
  return $script:Scratch
}

# --- Downloading --------------------------------------------------------------------------------
function Show-OfflineHelp {
@"
  If this is a network problem (offline, behind a proxy, or a firewall in the way):
  - Behind a proxy? Set HTTPS_PROXY (and HTTP_PROXY) in this window and run again.
  - Offline / locked-down? Install these yourself, then run again:
      .NET 10 SDK  https://dotnet.microsoft.com/download/dotnet/10.0
      Node.js 22   https://nodejs.org
"@ | Write-Host -ForegroundColor Yellow
}
function Fetch([string]$url, [string]$dest) {
  for ($i = 1; $i -le 3; $i++) {
    try { Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing -TimeoutSec 600; return $true }
    catch { if ($i -lt 3) { Start-Sleep -Seconds 2 } }
  }
  return $false
}
function Hash-Of([string]$algo, [string]$file) { (Get-FileHash -Algorithm $algo -Path $file).Hash.ToLowerInvariant() }
function Check-Disk([string]$dir, [int]$needMb, [string]$what) {
  try {
    $drive = (Get-Item $dir).PSDrive
    if ($drive -and $drive.Free -and [int]($drive.Free / 1MB) -lt $needMb) { Warn "Only $([int]($drive.Free / 1MB)) MB free for $what; it needs about $needMb MB." }
  } catch {}
}
function Expand-Zip([string]$zip, [string]$dest) {
  if (Have 'tar.exe') { & tar.exe -xf $zip -C $dest; if ($LASTEXITCODE -eq 0) { return } }   # tar ships with Windows 10 1803+ and is much faster
  Expand-Archive -Path $zip -DestinationPath $dest -Force
}
function Add-ToPath([string]$dir) { if (($env:Path -split ';') -notcontains $dir) { $env:Path = "$dir;$env:Path" } }

# Ask before downloading anything. Non-interactive runs (CI, pipes) must opt in with -Yes.
function Confirm-Install([string]$what, [string]$size) {
  if ($AUTO_INSTALL -eq 'yes') { return }
  $interactive = [Environment]::UserInteractive -and -not [Console]::IsInputRedirected
  if ($interactive) {
    $ans = Read-Host "? Download and install $what (about $size) into $TOOLS_DIR now? Nothing else on your system is touched. [Y/n]"
    if ($ans -match '^(|y|yes)$') { return }
    Die "Not installed (you answered no). Install $what yourself and run again, or run again and answer y."
  }
  Die "$what is missing and this is not an interactive terminal, so I cannot ask. Run again with -Yes to allow the download, or install it yourself."
}

# --- .NET SDK -----------------------------------------------------------------------------------
$env:DOTNET_NOLOGO = '1'; $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$script:DotnetFound = ''
function Test-DotnetOk {
  # Run from the API folder so global.json is honoured (it pins the 10.0 SDK).
  if (-not (Have 'dotnet')) { return $false }
  Push-Location (Join-Path $ROOT 'services\api')
  try { $v = (& dotnet --version 2>$null | Select-Object -First 1) } finally { Pop-Location }
  if ($LASTEXITCODE -ne 0 -or -not $v) { return $false }
  if (($v -split '\.')[0] -ne '10') { return $false }
  $script:DotnetFound = "$v".Trim(); return $true
}
function Use-LocalDotnet {
  $d = Join-Path $TOOLS_DIR 'dotnet'
  if (-not (Test-Path (Join-Path $d 'dotnet.exe'))) { return $false }
  $env:DOTNET_ROOT = $d; Add-ToPath $d; return $true
}
function Install-Dotnet {
  if ($ARCH -eq 'unsupported') { Die "This CPU ($archRaw) has no official .NET 10 SDK download. Install .NET 10 yourself: https://dotnet.microsoft.com/download/dotnet/10.0" }
  Step "Installing the .NET 10 SDK into $TOOLS_DIR\dotnet (about 250 MB; your system is not touched)..."
  New-Item -ItemType Directory -Force -Path $TOOLS_DIR | Out-Null; Check-Disk $TOOLS_DIR 1500 'the .NET SDK'
  $tmp = New-Scratch; $rid = "win-$ARCH"
  # No downloaded script is ever executed. Microsoft publishes the SHA-512 of the latest 10.0 SDK next to the
  # archive (its first line names the exact file); we download the archive and unpack it only if it matches.
  $meta = "https://aka.ms/dotnet/10.0/dotnet-sdk-$rid.zip"
  if (-not (Fetch "$meta.sha512" "$tmp\sdk.sha512")) { Show-OfflineHelp; Die 'Could not fetch the .NET SDK checksum from Microsoft.' }
  $line = ((Get-Content "$tmp\sdk.sha512" -TotalCount 1) -replace '[\uFEFF\r]', '').Trim()
  $parts = $line -split '\s+'; $want = $parts[0].ToLowerInvariant(); $file = $parts[$parts.Count - 1].TrimStart('*')
  if ($file -notmatch "^dotnet-sdk-(10\.0\.\d+)-$rid\.zip$") { Die "Unexpected .NET SDK checksum file from Microsoft ('$file'). Nothing was installed." }
  $ver = $Matches[1]
  if ($want -notmatch '^[0-9a-f]{128}$') { Die 'Malformed .NET SDK checksum from Microsoft. Nothing was installed.' }
  if (-not (Fetch "https://builds.dotnet.microsoft.com/dotnet/Sdk/$ver/$file" "$tmp\$file")) { Show-OfflineHelp; Die "Could not download $file." }
  $got = Hash-Of 'SHA512' "$tmp\$file"
  if ($got -ne $want) { Die "Checksum mismatch for $file (expected $want, got $got). The download is corrupt or tampered with; nothing was installed. Run again to retry." }
  # Unpack into a side folder first, so an interrupted install never leaves a half-built SDK behind.
  $partial = Join-Path $TOOLS_DIR 'dotnet.partial'
  if (Test-Path $partial) { Remove-Item -Recurse -Force $partial }
  New-Item -ItemType Directory -Force -Path $partial | Out-Null
  try { Expand-Zip "$tmp\$file" $partial } catch { Remove-Item -Recurse -Force $partial -ErrorAction SilentlyContinue; Die "Could not unpack $file." }
  $final = Join-Path $TOOLS_DIR 'dotnet'
  if (Test-Path $final) { Remove-Item -Recurse -Force $final }
  Move-Item $partial $final
  [void](Use-LocalDotnet)
  if (-not (Test-DotnetOk)) { Die 'The .NET SDK was installed but does not satisfy services\api\global.json (needs 10.0.x).' }
}
function Ensure-Dotnet {
  if (Test-DotnetOk) { Ok ".NET SDK $script:DotnetFound"; return }
  if ((Use-LocalDotnet) -and (Test-DotnetOk)) { Ok ".NET SDK $script:DotnetFound (installed by run.ps1 in $TOOLS_DIR\dotnet)"; return }
  $why = 'is not installed'
  if (Have 'dotnet') { $why = 'does not satisfy services\api\global.json (needs 10.0.x)' }
  if ($MODE -eq 'check') { Write-Host "- .NET 10 SDK $why - run.cmd will download it to $TOOLS_DIR\dotnet"; return }
  if ($AUTO_INSTALL -eq 'never') { Die "The .NET 10 SDK $why. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 (or drop -NoInstall so run.cmd can offer to do it)." }
  Write-Host "- The .NET 10 SDK $why."
  Confirm-Install 'the .NET 10 SDK' '250 MB'
  Install-Dotnet
  Ok ".NET SDK $script:DotnetFound (installed by run.ps1)"
}

# --- Node.js ------------------------------------------------------------------------------------
$script:NodeFound = ''
# Matches package.json "engines": ^20.19.0 || >=22.12.0 (Vite 7 needs it).
function Test-NodeVersion([string]$v) {
  if ($v -notmatch '^v?(\d+)\.(\d+)') { return $false }
  $ma = [int]$Matches[1]; $mi = [int]$Matches[2]
  return ($ma -ge 23) -or ($ma -eq 22 -and $mi -ge 12) -or ($ma -eq 20 -and $mi -ge 19)
}
function Test-NodeOk {
  if (-not ((Have 'node') -and (Have 'npm.cmd'))) { return $false }
  $v = (& node --version 2>$null | Select-Object -First 1)
  if ($LASTEXITCODE -ne 0 -or -not $v -or -not (Test-NodeVersion "$v")) { return $false }
  $script:NodeFound = "$v".Trim(); return $true
}
function Use-LocalNode {
  $d = Join-Path $TOOLS_DIR 'node'
  if (-not (Test-Path (Join-Path $d 'node.exe'))) { return $false }
  Add-ToPath $d; return $true
}
function Install-Node {
  if ($ARCH -eq 'unsupported') { Die "This CPU ($archRaw) has no official Node.js download. Install Node 22 LTS yourself: https://nodejs.org" }
  Step "Installing Node.js 22 LTS into $TOOLS_DIR\node (about 40 MB; your system is not touched)..."
  New-Item -ItemType Directory -Force -Path $TOOLS_DIR | Out-Null; Check-Disk $TOOLS_DIR 600 'Node.js'
  $tmp = New-Scratch
  $base = "https://nodejs.org/dist/latest-$NODE_LTS_LINE"
  if (-not (Fetch "$base/SHASUMS256.txt" "$tmp\SHASUMS256.txt")) { Show-OfflineHelp; Die 'Could not reach nodejs.org.' }
  $line = Get-Content "$tmp\SHASUMS256.txt" | Where-Object { $_ -match "\snode-v[0-9.]+-win-$ARCH\.zip$" } | Select-Object -First 1
  if (-not $line) { Die "nodejs.org lists no Node build for win-$ARCH. Install Node 22 LTS yourself: https://nodejs.org" }
  $parts = ($line.Trim() -split '\s+'); $sum = $parts[0].ToLowerInvariant(); $file = $parts[$parts.Count - 1]
  if (-not (Fetch "$base/$file" "$tmp\$file")) { Show-OfflineHelp; Die "Could not download $file." }
  $got = Hash-Of 'SHA256' "$tmp\$file"
  if ($got -ne $sum) { Die "Checksum mismatch for $file (expected $sum, got $got). The download is corrupt or tampered with; nothing was installed. Run again to retry." }
  $x = Join-Path $tmp 'nodex'; New-Item -ItemType Directory -Force -Path $x | Out-Null
  try { Expand-Zip "$tmp\$file" $x } catch { Die "Could not unpack $file." }
  $inner = Get-ChildItem $x -Directory | Select-Object -First 1
  if (-not $inner) { Die "Unexpected layout in $file." }
  $final = Join-Path $TOOLS_DIR 'node'
  if (Test-Path $final) { Remove-Item -Recurse -Force $final }
  Move-Item $inner.FullName $final
  [void](Use-LocalNode)
  if (-not (Test-NodeOk)) { Die 'Node.js was installed but does not run on this machine.' }
}
function Ensure-Node {
  if (Test-NodeOk) { Ok "Node $script:NodeFound"; return }
  if ((Use-LocalNode) -and (Test-NodeOk)) { Ok "Node $script:NodeFound (installed by run.ps1 in $TOOLS_DIR\node)"; return }
  $why = 'is not installed'
  if (Have 'node') { $why = 'is too old or too new a line (needs 20.19+ or 22.12+), or has no npm next to it' }
  if ($MODE -eq 'check') { Write-Host "- Node.js $why - run.cmd will download Node 22 LTS to $TOOLS_DIR\node"; return }
  if ($AUTO_INSTALL -eq 'never') { Die "Node.js $why. Install Node 22 LTS from https://nodejs.org (or drop -NoInstall so run.cmd can offer to do it)." }
  Write-Host "- Node.js $why."
  Confirm-Install 'Node.js 22 LTS' '40 MB'
  Install-Node
  Ok "Node $script:NodeFound (installed by run.ps1)"
}

# --- Ports --------------------------------------------------------------------------------------
function Get-ListenerPids([int]$port) {
  try { return @(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction Stop | Select-Object -ExpandProperty OwningProcess -Unique) }
  catch { return @() }
}
function Test-PortInUse([int]$port) {
  if ((Get-ListenerPids $port).Count -gt 0) { return $true }
  foreach ($addr in '127.0.0.1', '::1') {
    $c = New-Object Net.Sockets.TcpClient
    try { $iar = $c.BeginConnect($addr, $port, $null, $null); if ($iar.AsyncWaitHandle.WaitOne(300) -and $c.Connected) { return $true } }
    catch {} finally { $c.Close() }
  }
  return $false
}
function Get-ProcCmd([int]$procId) {
  try { return [string](Get-CimInstance Win32_Process -Filter "ProcessId=$procId" -ErrorAction Stop).CommandLine } catch { return '' }
}
# Make a port usable: stop a previous ServiceHub of ours that still holds it; if something else holds it, take
# the next free port - unless the user pinned the port, in which case refuse rather than guess.
function Claim-Port([string]$name, [int]$port, [bool]$fixed, [string]$pattern, [string]$label, [int]$otherPort) {
  $tries = 0
  while ($true) {
    $foreign = $false; $blocker = ''
    if ($port -eq $otherPort) { $foreign = $true; $blocker = 'the other ServiceHub port' }
    elseif (Test-PortInUse $port) {
      $pids = Get-ListenerPids $port
      if ($pids.Count -eq 0) { $foreign = $true }
      foreach ($p in $pids) {
        $cmd = Get-ProcCmd $p
        if ($cmd -and $cmd.IndexOf($pattern, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
          Write-Host "<- Stopping previous $label on :$port (pid $p)"; Stop-Tree $p
        } else { $foreign = $true; $blocker = "pid ${p}: $($cmd.Substring(0, [Math]::Min(100, $cmd.Length)))" }
      }
      if (-not $foreign) {
        for ($i = 0; $i -lt 100 -and (Test-PortInUse $port); $i++) { Start-Sleep -Milliseconds 100 }
        if (Test-PortInUse $port) { $foreign = $true; $blocker = "the previous $label would not stop" }
      }
    }
    if (-not $foreign) { break }
    if ($fixed) { if (-not $blocker) { $blocker = 'a program this script cannot identify' }; Die "Port $port is held by something else ($blocker). Free it, or pick another with SERVICEHUB_${name}_PORT." }
    $tries++; if ($tries -gt 30) { Die "Could not find a free port near $port for the $label." }
    $port++
  }
  return $port
}

function Test-Up([string]$url) {
  try { $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2; return ($r.StatusCode -ge 200 -and $r.StatusCode -lt 400) } catch { return $false }
}

# --- Docker mode --------------------------------------------------------------------------------
# For people who prefer a container: nothing to install except Docker itself (a system-level install this script
# will not do for you - it only checks and explains).
$DOCKER_PORT = 8080
$KEY_FILE = Join-Path $userHome '.servicehub\docker.key'
function Run-Docker {
  $check = ($MODE -eq 'check')
  if (-not (Have 'docker')) { Die 'Docker is not installed. Install Docker Desktop: https://docs.docker.com/desktop/setup/install/windows-install/ - then run  run -Docker  again. (No Docker? Run  run  without -Docker; it needs no Docker.)' }
  Ok ("Docker " + ((& docker --version 2>$null) -replace '^Docker version ([^,]*).*', '$1'))
  & docker info *> $null
  if ($LASTEXITCODE -ne 0) { Die 'Docker is installed but its daemon is not running. Start Docker Desktop, wait until it says running, and try again.' }
  Ok 'Docker daemon is running'
  $cv = & docker compose version 2>$null
  if ($LASTEXITCODE -ne 0) { Die "The Docker Compose v2 plugin is missing ('docker compose'). Update Docker Desktop: https://docs.docker.com/compose/install/" }
  Ok ($cv | Select-Object -First 1)
  if (-not ((Test-Path (Join-Path $ROOT 'docker-compose.yml')) -and (Test-Path (Join-Path $ROOT 'Dockerfile')))) { Die 'docker-compose.yml / Dockerfile not found next to run.ps1. Run this from the repository root.' }
  $savedKey = $env:SERVICEHUB_ENCRYPTION_KEY; if (-not $savedKey) { $env:SERVICEHUB_ENCRYPTION_KEY = 'probe' }   # compose refuses to even list without the key set
  $ours = & docker compose ps -q 2>$null
  $env:SERVICEHUB_ENCRYPTION_KEY = $savedKey
  if (-not $ours -and (Test-PortInUse $DOCKER_PORT)) { Die "Port $DOCKER_PORT is already in use by another program (not this ServiceHub container). Stop it, or change the host port in docker-compose.yml." }
  if ($ours) { Write-Host '- A ServiceHub container from this folder already exists; it will be rebuilt and restarted (your data volume is kept).' }

  # The encryption key protects every stored cloud credential, and losing it makes them unreadable - so it is
  # made once and kept, never regenerated behind your back.
  if ($env:SERVICEHUB_ENCRYPTION_KEY) { Ok 'Encryption key: taken from SERVICEHUB_ENCRYPTION_KEY' }
  elseif ((Test-Path $KEY_FILE) -and (Get-Item $KEY_FILE).Length -gt 0) { Ok "Encryption key: reusing $KEY_FILE" }
  elseif ($check) { Write-Host "- No encryption key yet - run.cmd would create one at $KEY_FILE (only you can read it)" }
  else {
    if ($AUTO_INSTALL -ne 'yes') {
      if (-not ([Environment]::UserInteractive -and -not [Console]::IsInputRedirected)) { Die "No encryption key exists and this is not an interactive terminal. Set SERVICEHUB_ENCRYPTION_KEY, or run again with -Yes to create one at $KEY_FILE." }
      $ans = Read-Host "? Create an encryption key and save it to $KEY_FILE (readable only by you)? Keep a backup: without it saved cloud connections cannot be read. [Y/n]"
      if ($ans -notmatch '^(|y|yes)$') { Die 'No key, no container. Set SERVICEHUB_ENCRYPTION_KEY yourself and run again.' }
    }
    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create(); $rng.GetBytes($bytes); $rng.Dispose()
    $hex = ($bytes | ForEach-Object { $_.ToString('x2') }) -join ''
    New-Item -ItemType Directory -Force -Path (Split-Path $KEY_FILE) | Out-Null
    [IO.File]::WriteAllText($KEY_FILE, $hex)
    & icacls.exe $KEY_FILE /inheritance:r /grant:r "${env:USERNAME}:(R,W)" *> $null   # only this user can read it
    Ok "Encryption key created at $KEY_FILE - back it up somewhere safe."
  }
  if ($check) { Ok 'Check finished. Run  run -Docker'; exit 0 }
  if (-not $env:SERVICEHUB_ENCRYPTION_KEY) { $env:SERVICEHUB_ENCRYPTION_KEY = (Get-Content $KEY_FILE -Raw).Trim() }

  Step 'Building and starting the container (first run builds the image - a few minutes)...'
  & docker compose up --build -d
  if ($LASTEXITCODE -ne 0) { Die 'docker compose failed (output above). See docs\DOCKER.md -> Troubleshooting.' }
  $waited = 0
  while (-not (Test-Up "http://localhost:$DOCKER_PORT/health/ready")) {
    Start-Sleep -Seconds 2; $waited += 2
    if ($waited -ge $READY_TIMEOUT) {
      & docker compose logs --tail 40 servicehub
      Die "Not ready after ${READY_TIMEOUT}s (last log lines above). See docs\DOCKER.md -> Troubleshooting."
    }
  }
  Write-Host ''; Write-Host '+ ServiceHub is ready in Docker' -ForegroundColor Green
  Write-Host "   Open http://localhost:$DOCKER_PORT      (try /demo/azure first: made-up data, nothing is sent anywhere)"
  Write-Host '   Logs: docker compose logs -f servicehub   |   Stop: docker compose down   (your data stays in the volume)'
  exit 0
}

# --- Preflight ----------------------------------------------------------------------------------
Write-Host "ServiceHub - checking this machine (Windows/$ARCH)..."
if ($API_PORT -eq $WEB_PORT) { Die "SERVICEHUB_API_PORT and SERVICEHUB_WEB_PORT are both $API_PORT; they must differ." }
if (-not ((Test-Path (Join-Path $ROOT 'package.json')) -and (Test-Path (Join-Path $ROOT 'services\api')))) { Die 'Run this script from the ServiceHub repository root (package.json and services\api not found next to it).' }
if ($ROOT.Length -gt 120) { Warn "This folder path is long ($($ROOT.Length) characters). If the build fails with 'path too long', clone closer to the drive root (e.g. C:\servicehub)." }

# Both routes are available when Docker is installed and running: ask once, in a terminal only. Scripts, CI and
# anyone who passed a mode flag (or -Yes) are never asked.
$explicit = $Docker -or $Local -or $ApiOnly -or $WebOnly -or $Check -or ($env:SERVICEHUB_RUN -in 'local', 'docker')
if ($env:SERVICEHUB_RUN -eq 'docker') { $Docker = [switch]$true }
if (-not $explicit -and $AUTO_INSTALL -ne 'yes' -and [Environment]::UserInteractive -and -not [Console]::IsInputRedirected -and (Have 'docker')) {
  & docker info *> $null
  if ($LASTEXITCODE -eq 0) {
    Write-Host 'Docker is installed and running here, so there are two ways to run ServiceHub:'
    Write-Host '  1) Locally   - runs from this source folder (may download .NET and Node, after asking)'
    Write-Host '  2) Docker    - nothing to install; builds and runs a container'
    $pick = Read-Host '? Choose 1 or 2 [1]'
    if ($pick -match '^(2|d|docker)$') { $Docker = [switch]$true }
    elseif ($pick -notmatch '^(|1|l|local)$') { Die 'Please answer 1 or 2.' }
    Write-Host '  (Skip this question next time with  run -Local  or  run -Docker.)'
  }
}
if ($Docker) { Run-Docker }

$NEED_API = $MODE -ne 'web'
$NEED_WEB = $MODE -ne 'api'

if ($NEED_API) { Ensure-Dotnet }
if ($NEED_WEB) { Ensure-Node }

if ($NEED_API) {
  $DATA_DIR = $env:SERVICEHUB_DATA_DIR; if (-not $DATA_DIR) { $DATA_DIR = Join-Path $ROOT 'services\api\src\ServiceHub.Api\data' }
  if ($MODE -ne 'check') {
    $writable = $false
    try {
      New-Item -ItemType Directory -Force -Path $DATA_DIR -ErrorAction Stop | Out-Null
      $probe = Join-Path $DATA_DIR ('.w' + [Guid]::NewGuid().ToString('N')); [IO.File]::WriteAllText($probe, 'x'); Remove-Item $probe -Force; $writable = $true
    } catch {}
    if (-not $writable) { Die "The data folder $DATA_DIR cannot be created or written to. Fix its permissions, or point SERVICEHUB_DATA_DIR at a folder you own." }
  } else { Ok "Data folder $DATA_DIR" }
}

if ($MODE -eq 'check') {
  foreach ($p in $API_PORT, $WEB_PORT) {
    if (Test-PortInUse $p) { Write-Host "- Port $p is in use (a previous ServiceHub is stopped; for any other program the next free port is used, or run.cmd refuses if you pinned the port)." }
    else { Ok "Port $p is free" }
  }
  Ok 'Check finished. Run .\run.cmd'
  exit 0
}

# --- Web dependencies ---------------------------------------------------------------------------
# Install the first time, again whenever the lockfile moves on (a `git pull` that changed it), and again if a
# previous install was cut short (Vite missing). A failed install is retried once from a clean slate.
function Ensure-NodeModules {
  $vite = Join-Path $ROOT 'node_modules\vite\bin\vite.js'
  $stamp = Join-Path $ROOT 'node_modules\.package-lock.json'
  if ((Test-Path $vite) -and (Test-Path $stamp) -and ((Get-Item (Join-Path $ROOT 'package-lock.json')).LastWriteTime -le (Get-Item $stamp).LastWriteTime)) { return }
  Step 'Installing web dependencies (npm ci)...'
  & npm.cmd ci --no-audit --no-fund
  if ($LASTEXITCODE -ne 0) {
    Warn 'npm ci failed; retrying once from a clean node_modules...'
    Remove-Item -Recurse -Force (Join-Path $ROOT 'node_modules') -ErrorAction SilentlyContinue
    & npm.cmd ci --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { Show-OfflineHelp; Die 'npm ci failed twice (output above). See docs\LOCAL-SETUP.md -> Troubleshooting.' }
  }
  if (-not (Test-Path $vite)) { Die 'Vite is still missing after npm ci. Delete node_modules and run again.' }
}
if ($NEED_WEB) { Ensure-NodeModules }

# --- API restore + build ------------------------------------------------------------------------
# Done up front, in the open, so a compile or package problem is reported as exactly that - and the readiness
# timeout below measures startup only, not a slow first build.
if ($NEED_API) {
  $proj = 'services\api\src\ServiceHub.Api'
  Step 'Restoring and building the API (first run downloads packages and compiles - a few minutes)...'
  & dotnet restore $proj --nologo -v q
  if ($LASTEXITCODE -ne 0) {
    Warn 'Package restore failed; retrying once...'; Start-Sleep -Seconds 3
    & dotnet restore $proj --nologo -v q
    if ($LASTEXITCODE -ne 0) { Show-OfflineHelp; Die 'dotnet restore failed (needs access to nuget.org). Output above.' }
  }
  & dotnet build $proj --no-restore --nologo -v q '-clp:ErrorsOnly,Summary'
  if ($LASTEXITCODE -ne 0) { Die 'The API did not build (errors above). If you changed code, fix it; otherwise open an issue with this output.' }
}

# --- Free the ports -----------------------------------------------------------------------------
if ($NEED_API) { $API_PORT = Claim-Port 'API' $API_PORT $API_FIXED 'ServiceHub.Api' 'API' 0 }
if ($NEED_WEB) { $WEB_PORT = Claim-Port 'WEB' $WEB_PORT $WEB_FIXED (Join-Path $ROOT 'node_modules') 'dev server' $API_PORT }
if ($NEED_API -and $API_PORT -ne $apiCfg.Port) { Write-Host "- Port $($apiCfg.Port) is taken by another program; the API will use :$API_PORT instead." }
if ($NEED_WEB -and $WEB_PORT -ne $webCfg.Port) { Write-Host "- Port $($webCfg.Port) is taken by another program; the dev server will use :$WEB_PORT instead." }


# --- Start, wait, supervise ---------------------------------------------------------------------
try {
  if ($NEED_API) {
    Step "Starting the API on http://localhost:$API_PORT"
    # Development unless you say otherwise: that is what supplies the throw-away dev encryption key.
    if ($env:SERVICEHUB_DATA_DIR) { $env:ServiceHub__DataDirectory = $env:SERVICEHUB_DATA_DIR }
    if (-not $env:ASPNETCORE_ENVIRONMENT) { $env:ASPNETCORE_ENVIRONMENT = 'Development' }
    $env:ASPNETCORE_URLS = "http://localhost:$API_PORT"
    $dotnetExe = (Get-Command dotnet).Source
    $script:Started += Start-Process -FilePath $dotnetExe -PassThru -NoNewWindow -WorkingDirectory $ROOT `
      -ArgumentList @('run', '--project', "`"$ROOT\services\api\src\ServiceHub.Api`"", '--no-build', '--no-launch-profile')
  }
  if ($NEED_WEB) {
    Step "Starting the web server on http://localhost:$WEB_PORT"
    $env:VITE_PROXY_TARGET = "http://localhost:$API_PORT"
    # Vite is started with node directly rather than through `npm run`, so stopping it is clean.
    $nodeExe = (Get-Command node).Source
    $script:Started += Start-Process -FilePath $nodeExe -PassThru -NoNewWindow -WorkingDirectory (Join-Path $ROOT 'apps\servicehub') `
      -ArgumentList @("`"$ROOT\node_modules\vite\bin\vite.js`"", '--port', "$WEB_PORT", '--strictPort')
  }
  foreach ($p in $script:Started) { [void]$p.Handle }   # keep the handle so ExitCode stays readable

  $announced = $false; $waited = 0
  while ($true) {
    # Stop everything as soon as either process exits, so a crashed API is not hidden behind a live dev server.
    foreach ($p in $script:Started) {
      if ($p.HasExited) {
        Write-Host "x A ServiceHub process exited (code $($p.ExitCode)) - the output above says why. See docs\LOCAL-SETUP.md -> Troubleshooting." -ForegroundColor Red
        exit 1
      }
    }
    if (-not $announced) {
      $ready = $true
      if ($NEED_API -and -not (Test-Up "http://localhost:$API_PORT/health/ready")) { $ready = $false }
      if ($ready -and $NEED_WEB -and -not (Test-Up "http://localhost:$WEB_PORT/")) { $ready = $false }
      # Through Vite too: proves the proxy reaches the API, which is the path the browser actually uses.
      if ($ready -and $NEED_WEB -and $NEED_API -and -not (Test-Up "http://localhost:$WEB_PORT/health/ready")) { $ready = $false }
      if ($ready) {
        $announced = $true
        Write-Host ''; Write-Host '+ ServiceHub is ready' -ForegroundColor Green
        if ($MODE -eq 'api') { Write-Host "   API  http://localhost:$API_PORT   (health: /health/ready)" }
        else { Write-Host "   Open http://localhost:$WEB_PORT      (try /demo/azure first: made-up data, nothing is sent anywhere)" }
        Write-Host '   Ctrl-C to stop.'; Write-Host ''
      } elseif ($waited -ge $READY_TIMEOUT) {
        Write-Host "x Not ready after ${READY_TIMEOUT}s. Look for an error above; see docs\LOCAL-SETUP.md -> Troubleshooting." -ForegroundColor Red
        exit 1
      }
    }
    Start-Sleep -Seconds 1
    $waited++
  }
}
finally {
  Cleanup
}
