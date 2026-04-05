# ServiceHub - Windows PowerShell Launcher
# Starts both the .NET API and React frontend in separate PowerShell windows.
#
# Requirements:
#   - .NET 10 SDK   (install: winget install Microsoft.DotNet.SDK.10)
#   - Node.js 20+   (install: winget install OpenJS.NodeJS.LTS)
#
# Usage:
#   .\run.ps1

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host ""
Write-Host "  ServiceHub - Windows Launcher" -ForegroundColor Cyan
Write-Host "  =============================" -ForegroundColor Cyan
Write-Host ""

# ─── Prerequisite: .NET 10 SDK ───────────────────────────────────────────────

$dotnetOk = $false
try {
    $dotnetVersion = (dotnet --version 2>$null)
    if ($dotnetVersion -and $dotnetVersion.StartsWith("10.")) {
        $dotnetOk = $true
        Write-Host "  [OK] .NET SDK $dotnetVersion" -ForegroundColor Green
    }
} catch { }

if (-not $dotnetOk) {
    Write-Host "  [MISSING] .NET 10 SDK not found." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Install it with winget:" -ForegroundColor Yellow
    Write-Host "    winget install Microsoft.DotNet.SDK.10" -ForegroundColor White
    Write-Host ""
    Write-Host "  Or download from: https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor White
    Write-Host ""
    Read-Host "  Press Enter to exit"
    exit 1
}

# ─── Prerequisite: Node.js 20+ ───────────────────────────────────────────────

$nodeOk = $false
try {
    $nodeVersion = (node --version 2>$null)
    if ($nodeVersion) {
        $nodeMajor = [int]($nodeVersion.TrimStart('v').Split('.')[0])
        if ($nodeMajor -ge 20) {
            $nodeOk = $true
            Write-Host "  [OK] Node.js $nodeVersion" -ForegroundColor Green
        }
    }
} catch { }

if (-not $nodeOk) {
    Write-Host "  [MISSING] Node.js 20+ not found." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Install it with winget:" -ForegroundColor Yellow
    Write-Host "    winget install OpenJS.NodeJS.LTS" -ForegroundColor White
    Write-Host ""
    Write-Host "  Or download from: https://nodejs.org/" -ForegroundColor White
    Write-Host ""
    Read-Host "  Press Enter to exit"
    exit 1
}

# ─── Generate appsettings.Local.json if missing ──────────────────────────────
# NOTE: appsettings.Local.json is git-ignored and only loaded in Development mode.
#       It does NOT affect Azure App Service deployments.

$localSettings = Join-Path $ScriptDir "services\api\src\ServiceHub.Api\appsettings.Local.json"

if (-not (Test-Path $localSettings)) {
    Write-Host ""
    Write-Host "  Generating local secrets..." -ForegroundColor Yellow

    # Generate a 32-byte random key using .NET crypto (no openssl required on Windows)
    $keyBytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $encryptionKey = [System.BitConverter]::ToString($keyBytes) -replace '-', ''

    $spaBytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $spaTokenSecret = [System.BitConverter]::ToString($spaBytes) -replace '-', ''

    $json = @"
{
  "Security": {
    "EncryptionKey": "$encryptionKey",
    "EnableConnectionStringEncryption": true,
    "SpaToken": {
      "Enabled": false
    },
    "Authentication": {
      "Enabled": false
    }
  },
  "ApplicationInsights": {
    "ConnectionString": ""
  }
}
"@
    Set-Content -Path $localSettings -Value $json -Encoding UTF8
    Write-Host "  [OK] Created appsettings.Local.json with local encryption key" -ForegroundColor Green
} else {
    Write-Host "  [OK] appsettings.Local.json already exists - keeping existing secrets" -ForegroundColor Green
}

# ─── Install npm packages if needed ──────────────────────────────────────────

$nodeModules = Join-Path $ScriptDir "apps\web\node_modules"
if (-not (Test-Path $nodeModules)) {
    Write-Host ""
    Write-Host "  Installing npm packages..." -ForegroundColor Yellow
    Push-Location (Join-Path $ScriptDir "apps\web")
    npm install
    Pop-Location
}

Write-Host ""
Write-Host "  Starting services..." -ForegroundColor Cyan
Write-Host ""

# ─── Start the .NET API in a new PowerShell window ───────────────────────────

$apiDir = Join-Path $ScriptDir "services\api"
$apiCommand = "cd '$apiDir'; " +
              "`$env:ASPNETCORE_ENVIRONMENT='Development'; " +
              "dotnet run --project src/ServiceHub.Api/ServiceHub.Api.csproj --urls http://localhost:5153; " +
              "Read-Host 'API stopped. Press Enter to close'"

Start-Process powershell -ArgumentList "-NoExit", "-Command", $apiCommand

# Give the API a moment to start before launching the UI
Start-Sleep -Seconds 2

# ─── Start the React frontend in a new PowerShell window ─────────────────────

$webDir = Join-Path $ScriptDir "apps\web"
$webCommand = "cd '$webDir'; npm run dev -- --port 3000; Read-Host 'UI stopped. Press Enter to close'"

Start-Process powershell -ArgumentList "-NoExit", "-Command", $webCommand

# ─── Print URLs ───────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "  ============================================" -ForegroundColor Green
Write-Host "  Services are starting in separate windows." -ForegroundColor Green
Write-Host "  ============================================" -ForegroundColor Green
Write-Host ""
Write-Host "  API:          http://localhost:5153"       -ForegroundColor White
Write-Host "  API Docs:     http://localhost:5153/scalar/v1" -ForegroundColor White
Write-Host "  UI:           http://localhost:3000"       -ForegroundColor White
Write-Host ""
Write-Host "  Wait about 10 seconds for both services to start," -ForegroundColor Gray
Write-Host "  then open http://localhost:3000 in your browser."  -ForegroundColor Gray
Write-Host ""
Write-Host "  Close both PowerShell windows to stop all services." -ForegroundColor Gray
Write-Host ""
