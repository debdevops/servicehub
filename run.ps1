# ServiceHub - Windows PowerShell Launcher
# Starts both the .NET API and React frontend in separate PowerShell windows.
#
# Requirements:
#   - .NET 10 SDK   (auto-installed via winget if available)
#   - Node.js 20+   (auto-installed via winget if available)
#
# Usage:
#   .\run.ps1

$ErrorActionPreference = "Continue"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host ""
Write-Host "╔════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║     ServiceHub - Windows Launcher      ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Helper function to check if tool exists
function Test-ToolExists {
    param([string]$toolName)
    try {
        $version = & $toolName --version 2>$null
        return $null -ne $version
    }
    catch {
        return $false
    }
}

# Helper function to install via winget
function Install-WithWinget {
    param([string]$packageId, [string]$displayName)
    
    # Check if winget is available
    try {
        $wingetVersion = & winget --version 2>$null
        if ($null -eq $wingetVersion) {
            return $false
        }
    }
    catch {
        return $false
    }
    
    Write-Host "  Installing $displayName via winget..." -ForegroundColor Yellow
    try {
        & winget install --accept-source-agreements --accept-package-agreements $packageId 2>&1 | Out-Null
        Write-Host "  [OK] $displayName installed via winget" -ForegroundColor Green
        # Refresh PATH
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
        return $true
    }
    catch {
        Write-Host "  [FAILED] Could not install via winget" -ForegroundColor Red
        return $false
    }
}

# ─── Prerequisite: .NET 10 SDK ───────────────────────────────────────────────

$dotnetOk = $false
try {
    $dotnetVersion = (dotnet --version 2>$null)
    if ($dotnetVersion -and $dotnetVersion.StartsWith("10.")) {
        $dotnetOk = $true
        Write-Host "  ✓ .NET SDK $dotnetVersion" -ForegroundColor Green
    }
    else {
        Write-Host "  ✗ .NET 10 SDK not found (or wrong version: $dotnetVersion)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  ✗ .NET SDK not found" -ForegroundColor Yellow
}

if (-not $dotnetOk) {
    $installed = $false
    
    # Try to install via winget
    if (Install-WithWinget "Microsoft.DotNet.SDK.10" ".NET 10 SDK") {
        # Verify installation
        try {
            $dotnetVersion = (dotnet --version 2>$null)
            if ($dotnetVersion -and $dotnetVersion.StartsWith("10.")) {
                $dotnetOk = $true
                $installed = $true
                Write-Host "  ✓ .NET SDK verified after installation" -ForegroundColor Green
            }
        }
        catch { }
    }
    
    if (-not $installed) {
        Write-Host ""
        Write-Host "  [ERROR] .NET 10 SDK is required but could not be auto-installed." -ForegroundColor Red
        Write-Host ""
        Write-Host "  Manual installation options:" -ForegroundColor Yellow
        Write-Host "    1. winget: winget install Microsoft.DotNet.SDK.10" -ForegroundColor White
        Write-Host "    2. Download: https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor White
        Write-Host ""
        Read-Host "  Press Enter to exit"
        exit 1
    }
}

# ─── Prerequisite: Node.js 20+ ───────────────────────────────────────────────

$nodeOk = $false
try {
    $nodeVersion = (node --version 2>$null)
    if ($nodeVersion) {
        $nodeMajor = [int]($nodeVersion.TrimStart('v').Split('.')[0])
        if ($nodeMajor -ge 20) {
            $nodeOk = $true
            Write-Host "  ✓ Node.js $nodeVersion" -ForegroundColor Green
        }
        else {
            Write-Host "  ✗ Node.js version too old: $nodeVersion (need 20+)" -ForegroundColor Yellow
        }
    }
} catch {
    Write-Host "  ✗ Node.js not found" -ForegroundColor Yellow
}

if (-not $nodeOk) {
    $installed = $false
    
    # Try to install via winget
    if (Install-WithWinget "OpenJS.NodeJS.LTS" "Node.js LTS") {
        # Verify installation
        try {
            $nodeVersion = (node --version 2>$null)
            if ($nodeVersion) {
                $nodeMajor = [int]($nodeVersion.TrimStart('v').Split('.')[0])
                if ($nodeMajor -ge 20) {
                    $nodeOk = $true
                    $installed = $true
                    Write-Host "  ✓ Node.js verified after installation" -ForegroundColor Green
                }
            }
        }
        catch { }
    }
    
    if (-not $installed) {
        Write-Host ""
        Write-Host "  [ERROR] Node.js 20+ is required but could not be auto-installed." -ForegroundColor Red
        Write-Host ""
        Write-Host "  Manual installation options:" -ForegroundColor Yellow
        Write-Host "    1. winget: winget install OpenJS.NodeJS.LTS" -ForegroundColor White
        Write-Host "    2. Download: https://nodejs.org/" -ForegroundColor White
        Write-Host ""
        Read-Host "  Press Enter to exit"
        exit 1
    }
}

# ─── Generate appsettings.Local.json if missing ──────────────────────────────
# NOTE: appsettings.Local.json is git-ignored and only loaded in Development mode.
#       It does NOT affect Azure App Service deployments.

Write-Host ""
Write-Host "  Configuring local environment..." -ForegroundColor Cyan

$localSettings = Join-Path $ScriptDir "services\api\src\ServiceHub.Api\appsettings.Local.json"

if (-not (Test-Path $localSettings)) {
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
    Write-Host "  ✓ Created appsettings.Local.json with local encryption key" -ForegroundColor Green
} else {
    Write-Host "  ✓ appsettings.Local.json already exists - keeping existing secrets" -ForegroundColor Green
}

# ─── Restore .NET packages ──────────────────────────────────────────────────

Write-Host ""
Write-Host "  Installing dependencies..." -ForegroundColor Cyan

$apiDir = Join-Path $ScriptDir "services\api"
Push-Location $apiDir
Write-Host "  Restoring .NET packages..." -ForegroundColor Yellow
dotnet restore ServiceHub.sln 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ .NET packages restored" -ForegroundColor Green
}
else {
    Write-Host "  ✗ .NET package restore failed" -ForegroundColor Red
    Pop-Location
    Read-Host "  Press Enter to exit"
    exit 1
}
Pop-Location

# ─── Install npm packages if needed ──────────────────────────────────────────

$nodeModules = Join-Path $ScriptDir "apps\web\node_modules"
if (-not (Test-Path $nodeModules)) {
    Write-Host "  Installing npm packages (this may take 2-3 minutes)..." -ForegroundColor Yellow
    $webDir = Join-Path $ScriptDir "apps\web"
    Push-Location $webDir
    
    if (Test-Path "package-lock.json") {
        npm ci --legacy-peer-deps 2>&1 | Out-Null
    }
    else {
        npm install --legacy-peer-deps 2>&1 | Out-Null
    }
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ npm packages installed" -ForegroundColor Green
    }
    else {
        Write-Host "  ✗ npm package installation failed" -ForegroundColor Red
        Pop-Location
        Read-Host "  Press Enter to exit"
        exit 1
    }
    Pop-Location
}
else {
    Write-Host "  ✓ npm packages already installed" -ForegroundColor Green
}

Write-Host ""
Write-Host "  Starting services..." -ForegroundColor Cyan
Write-Host ""

# ─── Start the .NET API in a new PowerShell window ───────────────────────────

$apiDir = Join-Path $ScriptDir "services\api"
$apiLogFile = "$env:TEMP\servicehub_api.log"
$apiCommand = @"
`$env:ASPNETCORE_ENVIRONMENT='Development'
`$env:ASPNETCORE_URLS='http://localhost:5153'
cd '$apiDir'
Write-Host "  [API] Starting .NET backend..." -ForegroundColor Cyan
dotnet run --project src/ServiceHub.Api/ServiceHub.Api.csproj --urls http://localhost:5153 *>&1 | Tee-Object -FilePath '$apiLogFile' | ForEach-Object { Write-Host "  [API] `$`_" }
Write-Host ""
Write-Host "  [API] Service stopped." -ForegroundColor Yellow
Read-Host "  Press Enter to close this window"
"@

Write-Host "  ✓ Starting API server (PID will be shown in its window)..." -ForegroundColor Green
$apiProcess = Start-Process powershell -PassThru -ArgumentList "-NoExit", "-Command", $apiCommand
$apiPID = $apiProcess.Id

# Give the API a moment to start before checking
Start-Sleep -Seconds 3

# ─── Start the React frontend in a new PowerShell window ─────────────────────

$webDir = Join-Path $ScriptDir "apps\web"
$webLogFile = "$env:TEMP\servicehub_ui.log"
$webCommand = @"
cd '$webDir'
Write-Host "  [UI] Starting React frontend..." -ForegroundColor Cyan
npm run dev -- --port 3000 *>&1 | Tee-Object -FilePath '$webLogFile' | ForEach-Object { Write-Host "  [UI] `$`_" }
Write-Host ""
Write-Host "  [UI] Service stopped." -ForegroundColor Yellow
Read-Host "  Press Enter to close this window"
"@

Write-Host "  ✓ Starting UI server (PID will be shown in its window)..." -ForegroundColor Green
$webProcess = Start-Process powershell -PassThru -ArgumentList "-NoExit", "-Command", $webCommand
$webPID = $webProcess.Id

# Wait a bit for services to start
Start-Sleep -Seconds 3

# ─── Print URLs ───────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "╔════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║  ✓ Services Started in New Windows     ║" -ForegroundColor Green  
Write-Host "╚════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "  📍 API Server:" -ForegroundColor Cyan
Write-Host "      • HTTP:  http://localhost:5153" -ForegroundColor White
Write-Host "      • Docs:  http://localhost:5153/scalar/v1" -ForegroundColor White
Write-Host ""
Write-Host "  🌐 React Frontend:" -ForegroundColor Cyan
Write-Host "      • http://localhost:3000" -ForegroundColor White
Write-Host ""
Write-Host "  📋 Process IDs:" -ForegroundColor Cyan
Write-Host "      • API: $apiPID" -ForegroundColor White
Write-Host "      • UI:  $webPID" -ForegroundColor White
Write-Host ""
Write-Host "  ════════════════════════════════════════" -ForegroundColor Gray
Write-Host "  Wait 10-20 seconds for both to fully start," -ForegroundColor Gray
Write-Host "  then open http://localhost:3000 in your browser." -ForegroundColor Gray
Write-Host ""
Write-Host "  📝 Logs saved to:" -ForegroundColor Cyan
Write-Host "      • API: $apiLogFile" -ForegroundColor DarkGray
Write-Host "      • UI:  $webLogFile" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  ⚠️  To stop all services, close both PowerShell windows." -ForegroundColor Yellow
Write-Host ""

# Keep this window open
Write-Host "  Press Ctrl+C or close this window to stop monitoring..." -ForegroundColor Yellow
try {
    # Wait for either process to exit
    while ($true) {
        if (-not (Test-Path "\\.\pipe\$apiPID" -ErrorAction SilentlyContinue) -or 
            -not (Test-Path "\\.\pipe\$webPID" -ErrorAction SilentlyContinue)) {
            Start-Sleep -Seconds 1
        }
        
        # Check if processes are still running
        try {
            $apiRunning = Get-Process -Id $apiPID -ErrorAction SilentlyContinue
            $webRunning = Get-Process -Id $webPID -ErrorAction SilentlyContinue
            
            if (-not $apiRunning -and -not $webRunning) {
                Write-Host ""
                Write-Host "  ℹ Both services have stopped." -ForegroundColor Yellow
                break
            }
        }
        catch { }
        
        Start-Sleep -Seconds 5
    }
}
catch {
    # User pressed Ctrl+C
}

Write-Host ""
Write-Host "  Monitoring stopped. You can still interact with the service windows." -ForegroundColor Cyan
Write-Host ""
