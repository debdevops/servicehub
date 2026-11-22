# ServiceHub for Windows: the same job as run.sh, in PowerShell. No WSL, no admin rights, nothing system-wide.
#
#   git clone https://github.com/debdevops/servicehub.git
#   cd servicehub
#   .\run.ps1            (PowerShell)        or        run.cmd            (Command Prompt, or double-click)
#
# It checks your machine, downloads the .NET 10 SDK and Node.js into %USERPROFILE%\.servicehub\tools if you do not have them
# (both archives are verified against the checksum their publisher gives before they are unpacked), installs the packages,
# builds the API, starts the API and the web server, and waits until they answer. Press Ctrl-C to stop everything it started.
#
#   .\run.ps1 -Check        say what would be installed, change nothing
#   .\run.ps1 -NoInstall    never download anything (you installed .NET 10 and Node.js yourself)
#
# Settings (environment variables, optional): SERVICEHUB_API_PORT (5153), SERVICEHUB_WEB_PORT (3000), SERVICEHUB_DATA_DIR,
# SERVICEHUB_TOOLS_DIR, SERVICEHUB_READY_TIMEOUT (180 seconds). If a port is busy and you did not set it, the next free one is used.
#
# The help button (ServiceHub Bot) is not started by this script; ServiceHub works fully without it.
[CmdletBinding()]
param([switch]$Check, [switch]$NoInstall)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Windows PowerShell 5.1 downloads are very slow with the progress bar on
try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch {}

$Root = $PSScriptRoot
Set-Location $Root
function Say($m)  { Write-Host $m }
function Step($m) { Write-Host "> $m" }
function Warn($m) { Write-Host "! $m" -ForegroundColor Yellow }
function Die($m)  { Write-Host "X $m" -ForegroundColor Red; exit 1 }

$HomeDir  = if ($env:USERPROFILE) { $env:USERPROFILE } else { $Root }
$Tools    = if ($env:SERVICEHUB_TOOLS_DIR) { $env:SERVICEHUB_TOOLS_DIR } else { Join-Path $HomeDir '.servicehub\tools' }
$ApiPort  = if ($env:SERVICEHUB_API_PORT) { [int]$env:SERVICEHUB_API_PORT } else { 5153 }
$WebPort  = if ($env:SERVICEHUB_WEB_PORT) { [int]$env:SERVICEHUB_WEB_PORT } else { 3000 }
$ApiFixed = [bool]$env:SERVICEHUB_API_PORT
$WebFixed = [bool]$env:SERVICEHUB_WEB_PORT
$Timeout  = if ($env:SERVICEHUB_READY_TIMEOUT) { [int]$env:SERVICEHUB_READY_TIMEOUT } else { 180 }
$AutoInstall = -not $NoInstall -and $env:SERVICEHUB_AUTO_INSTALL -ne '0'
$Arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
if ($env:PROCESSOR_ARCHITECTURE -eq 'x86' -and -not $env:PROCESSOR_ARCHITEW6432) { Die 'This is a 32-bit Windows. .NET 10 and ServiceHub need 64-bit Windows.' }

function Has($n) { [bool](Get-Command $n -ErrorAction SilentlyContinue) }
function Download($url, $dest) {
  for ($i = 1; $i -le 3; $i++) {
    try { Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing; return } catch { if ($i -eq 3) { throw } ; Start-Sleep -Seconds 3 }
  }
}
function OfflineHelp { Say '  Cannot reach the internet or a download site? Install .NET 10 (https://dotnet.microsoft.com/download/dotnet/10.0) and Node.js (https://nodejs.org) yourself, then run:  .\run.ps1 -NoInstall' }

# ---------- .NET 10 SDK ----------
function DotnetOk {
  if (-not (Has 'dotnet')) { return $false }
  Push-Location (Join-Path $Root 'services\api')   # global.json is honoured from here
  try { $v = (& dotnet --version 2>$null) } catch { $v = $null } finally { Pop-Location }
  if (-not $v -or -not ($v -match '^10\.')) { return $false }
  $script:DotnetFound = $v; return $true
}
function UseLocalDotnet {
  $d = Join-Path $Tools 'dotnet'
  if (-not (Test-Path (Join-Path $d 'dotnet.exe'))) { return $false }
  $env:DOTNET_ROOT = $d; $env:PATH = "$d;$env:PATH"; return $true
}
function InstallDotnet {
  Step "Installing the .NET 10 SDK into $Tools\dotnet (about 300 MB; your system is not touched)..."
  New-Item -ItemType Directory -Force -Path $Tools | Out-Null
  $tmp = Join-Path ([IO.Path]::GetTempPath()) ('servicehub-run-' + [guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Force -Path $tmp | Out-Null
  try {
    # Microsoft publishes the SHA-512 of the latest 10.0 SDK next to the archive; its first line names the exact file.
    $meta = "https://aka.ms/dotnet/10.0/dotnet-sdk-win-$Arch.zip"
    try { Download "$meta.sha512" "$tmp\sdk.sha512" } catch { OfflineHelp; Die 'Could not fetch the .NET SDK checksum from Microsoft.' }
    $line = (Get-Content "$tmp\sdk.sha512" -TotalCount 1).Trim()
    $parts = $line -split '\s+'
    $want = $parts[0].ToLower(); $file = $parts[-1]
    if ($file -notmatch "^dotnet-sdk-10\.0\.\d+-win-$Arch\.zip$") { Die "Unexpected .NET SDK checksum file from Microsoft ('$file'). Nothing was installed." }
    if ($want -notmatch '^[0-9a-f]{128}$') { Die 'Malformed .NET SDK checksum from Microsoft. Nothing was installed.' }
    $ver = ($file -replace '^dotnet-sdk-(10\.0\.\d+)-.*', '$1')
    try { Download "https://builds.dotnet.microsoft.com/dotnet/Sdk/$ver/$file" "$tmp\$file" } catch { OfflineHelp; Die "Could not download $file." }
    $got = (Get-FileHash "$tmp\$file" -Algorithm SHA512).Hash.ToLower()
    if ($got -ne $want) { Die "Checksum mismatch for $file. The download is corrupt or tampered with; nothing was installed. Run again to retry." }
    $part = Join-Path $Tools 'dotnet.partial'
    if (Test-Path $part) { Remove-Item -Recurse -Force $part }
    Step 'Unpacking (a minute or two)...'
    Expand-Archive -Path "$tmp\$file" -DestinationPath $part -Force
    $final = Join-Path $Tools 'dotnet'
    if (Test-Path $final) { Remove-Item -Recurse -Force $final }
    Move-Item $part $final
  } finally { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }
  if (-not (UseLocalDotnet) -or -not (DotnetOk)) { Die 'The .NET SDK was installed but does not satisfy services\api\global.json (needs 10.0.x).' }
}
function EnsureDotnet {
  if (DotnetOk) { Say "OK .NET SDK $script:DotnetFound"; return }
  if ((UseLocalDotnet) -and (DotnetOk)) { Say "OK .NET SDK $script:DotnetFound (installed by run.ps1 in $Tools\dotnet)"; return }
  if ($Check) { Say "- The .NET 10 SDK is missing or the wrong version: .\run.ps1 will download it to $Tools\dotnet"; return }
  if (-not $AutoInstall) { Die 'The .NET 10 SDK is missing or the wrong version. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 (or drop -NoInstall to let run.ps1 do it).' }
  Say '- The .NET 10 SDK is missing or the wrong version.'
  InstallDotnet
  Say "OK .NET SDK $script:DotnetFound (installed by run.ps1)"
}

# ---------- Node.js ----------
function NodeOk {
  if (-not ((Has 'node') -and (Has 'npm'))) { return $false }
  $v = (& node --version 2>$null) -replace '^v', ''
  if (-not $v) { return $false }
  $p = $v.Split('.'); $maj = [int]$p[0]; $min = [int]$p[1]
  if (($maj -eq 20 -and $min -ge 19) -or ($maj -eq 22 -and $min -ge 12) -or $maj -ge 23) { $script:NodeFound = $v; return $true }
  return $false
}
function UseLocalNode {
  $d = Join-Path $Tools 'node'
  if (-not (Test-Path (Join-Path $d 'node.exe'))) { return $false }
  $env:PATH = "$d;$env:PATH"; return $true
}
function InstallNode {
  Step "Installing Node.js 24 LTS into $Tools\node (about 40 MB; your system is not touched)..."
  New-Item -ItemType Directory -Force -Path $Tools | Out-Null
  $tmp = Join-Path ([IO.Path]::GetTempPath()) ('servicehub-run-' + [guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Force -Path $tmp | Out-Null
  try {
    $base = 'https://nodejs.org/dist/latest-v24.x'
    try { Download "$base/SHASUMS256.txt" "$tmp\SHASUMS256.txt" } catch { OfflineHelp; Die 'Could not reach nodejs.org.' }
    $m = Select-String -Path "$tmp\SHASUMS256.txt" -Pattern " (node-v[0-9.]+-win-$Arch\.zip)$" | Select-Object -First 1
    if (-not $m) { Die "nodejs.org lists no Windows $Arch build of Node 24. Nothing was installed." }
    $want = ($m.Line -split '\s+')[0].ToLower(); $file = $m.Matches[0].Groups[1].Value
    try { Download "$base/$file" "$tmp\$file" } catch { OfflineHelp; Die "Could not download $file." }
    $got = (Get-FileHash "$tmp\$file" -Algorithm SHA256).Hash.ToLower()
    if ($got -ne $want) { Die "Checksum mismatch for $file. The download is corrupt or tampered with; nothing was installed. Run again to retry." }
    $part = Join-Path $Tools 'node.partial'
    if (Test-Path $part) { Remove-Item -Recurse -Force $part }
    Expand-Archive -Path "$tmp\$file" -DestinationPath $part -Force
    $inner = Get-ChildItem $part | Select-Object -First 1
    $final = Join-Path $Tools 'node'
    if (Test-Path $final) { Remove-Item -Recurse -Force $final }
    Move-Item $inner.FullName $final
    Remove-Item -Recurse -Force $part -ErrorAction SilentlyContinue
  } finally { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }
  if (-not (UseLocalNode) -or -not (NodeOk)) { Die 'Node.js was installed but does not run. Install Node.js 22 or 24 from https://nodejs.org and run again.' }
}
function EnsureNode {
  if (NodeOk) { Say "OK Node.js $script:NodeFound"; return }
  if ((UseLocalNode) -and (NodeOk)) { Say "OK Node.js $script:NodeFound (installed by run.ps1 in $Tools\node)"; return }
  if ($Check) { Say "- Node.js is missing or too old (needs 20.19+ or 22.12+): .\run.ps1 will download Node 24 LTS to $Tools\node"; return }
  if (-not $AutoInstall) { Die 'Node.js is missing or too old (needs 20.19+ or 22.12+). Install it from https://nodejs.org (or drop -NoInstall to let run.ps1 do it).' }
  Say '- Node.js is missing or too old.'
  InstallNode
  Say "OK Node.js $script:NodeFound (installed by run.ps1)"
}

# ---------- Ports ----------
function PortFree([int]$p) {
  $l = $null
  try { $l = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $p); $l.Start(); return $true } catch { return $false } finally { if ($l) { try { $l.Stop() } catch {} } }
}
function ClaimPort([int]$port, [bool]$fixed, [string]$what, [string]$var) {
  if (PortFree $port) { return $port }
  if ($fixed) { Die "Port $port (the $what) is already in use by another program. Close it, or choose another port with $var." }
  $n = $port
  for ($i = 0; $i -lt 50; $i++) { $n++; if (PortFree $n) { Warn "Port $port is busy, so the $what uses $n instead."; return $n } }
  Die "No free port found near $port for the $what."
}

# ---------- Checks ----------
Say "ServiceHub (Windows) - checking this machine"
Say "OK Windows $([Environment]::OSVersion.Version), $Arch"
EnsureDotnet
EnsureNode
$DataDir = if ($env:SERVICEHUB_DATA_DIR) { $env:SERVICEHUB_DATA_DIR } else { Join-Path $Root 'services\api\src\ServiceHub.Api\data' }
try { New-Item -ItemType Directory -Force -Path $DataDir | Out-Null; $t = Join-Path $DataDir '.write-test'; Set-Content $t 'x'; Remove-Item $t } catch { Die "The data folder $DataDir cannot be created or written to. Fix its permissions, or point SERVICEHUB_DATA_DIR at another folder." }
Say "OK data folder $DataDir"
if ($ApiPort -eq $WebPort) { Die "SERVICEHUB_API_PORT and SERVICEHUB_WEB_PORT are both $ApiPort; they must differ." }
if ($Check) { Say ''; Say 'Check only: nothing was installed or started.'; exit 0 }

# ---------- Packages and build ----------
if (-not (Test-Path (Join-Path $Root 'node_modules\vite\bin\vite.js'))) {
  Step 'Installing web dependencies (npm ci)...'
  & npm ci --no-audit --no-fund
  if ($LASTEXITCODE -ne 0) {
    Warn 'npm ci failed; retrying once from a clean node_modules...'
    if (Test-Path (Join-Path $Root 'node_modules')) { Remove-Item -Recurse -Force (Join-Path $Root 'node_modules') }
    & npm ci --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { OfflineHelp; Die 'npm ci failed twice (output above). See docs\LOCAL-SETUP.md, Troubleshooting.' }
  }
  if (-not (Test-Path (Join-Path $Root 'node_modules\vite\bin\vite.js'))) { Die 'Vite is still missing after npm ci. Delete the node_modules folder and run again.' }
}
$Proj = 'services\api\src\ServiceHub.Api'
Step 'Restoring and building the API (first run downloads packages and compiles: a few minutes)...'
& dotnet restore $Proj --nologo -v q
if ($LASTEXITCODE -ne 0) { Warn 'Package restore failed; retrying once...'; Start-Sleep -Seconds 3; & dotnet restore $Proj --nologo -v q; if ($LASTEXITCODE -ne 0) { OfflineHelp; Die 'dotnet restore failed (needs access to nuget.org). Output above.' } }
& dotnet build $Proj --no-restore --nologo -v q '-clp:ErrorsOnly;Summary'
if ($LASTEXITCODE -ne 0) { Die 'The API did not build (errors above). If you changed code, fix it; otherwise open an issue with this output.' }

$ApiPort = ClaimPort $ApiPort $ApiFixed 'API' 'SERVICEHUB_API_PORT'
$WebPort = ClaimPort $WebPort $WebFixed 'web server' 'SERVICEHUB_WEB_PORT'

# ---------- Start ----------
$procs = @()
function StopAll {
  foreach ($p in $procs) { if ($p -and -not $p.HasExited) { & taskkill /T /F /PID $p.Id *> $null } }
}
function Up($url) { try { $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2; return ($r.StatusCode -ge 200 -and $r.StatusCode -lt 400) } catch { return $false } }

try {
  Step "Starting the API on http://localhost:$ApiPort"
  # Development supplies a throw-away encryption key. Without it the API starts as Production and, correctly, refuses to run with no key.
  if (-not $env:ASPNETCORE_ENVIRONMENT) { $env:ASPNETCORE_ENVIRONMENT = 'Development' }
  $env:ASPNETCORE_URLS = "http://localhost:$ApiPort"
  if ($env:SERVICEHUB_DATA_DIR) { $env:ServiceHub__DataDirectory = $env:SERVICEHUB_DATA_DIR }
  $procs += Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--project', $Proj, '--no-build', '--no-launch-profile') -WorkingDirectory $Root -NoNewWindow -PassThru

  Step "Starting the web server on http://localhost:$WebPort"
  $env:VITE_PROXY_TARGET = "http://localhost:$ApiPort"
  $env:VITE_SERVICEHUB_BOT = 'false'
  $vite = Join-Path $Root 'node_modules\vite\bin\vite.js'
  $procs += Start-Process -FilePath 'node' -ArgumentList @("`"$vite`"", '--port', "$WebPort", '--strictPort') -WorkingDirectory (Join-Path $Root 'apps\servicehub') -NoNewWindow -PassThru

  $announced = $false; $waited = 0
  while ($true) {
    foreach ($p in $procs) {
      if ($p.HasExited) { Write-Host 'X A ServiceHub process exited: the output above says why. See docs\LOCAL-SETUP.md, Troubleshooting.' -ForegroundColor Red; StopAll; exit 1 }
    }
    if (-not $announced) {
      if ((Up "http://localhost:$ApiPort/health/ready") -and (Up "http://localhost:$WebPort/") -and (Up "http://localhost:$WebPort/health/ready")) {
        $announced = $true
        Say ''
        Say 'OK ServiceHub is ready'
        Say "   Open http://localhost:$WebPort      (choose 'Try it with sample data': made-up data, nothing is sent anywhere)"
        Say '   Ctrl-C to stop.'
        Say ''
      } elseif ($waited -ge $Timeout) {
        Write-Host "X Not ready after $Timeout s. Look for an error above; see docs\LOCAL-SETUP.md, Troubleshooting." -ForegroundColor Red; StopAll; exit 1
      }
    }
    Start-Sleep -Seconds 1; $waited++
  }
} finally { StopAll }
