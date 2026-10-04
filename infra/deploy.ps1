<#
.SYNOPSIS
  ServiceHub - deploy to your own Azure, AWS or Google Cloud account (Windows).
  macOS and Linux: use deploy.sh, which does the same thing.

.DESCRIPTION
  .\deploy.ps1 -Cloud <azure|aws|gcp>             deploy (asks a few questions the first time)
  .\deploy.ps1 -Cloud <cloud> -Doctor             only check that this computer is ready
  .\deploy.ps1 -Cloud <cloud> -Plan               show what would be created; change nothing
  .\deploy.ps1 -Cloud <cloud> -Destroy            remove ServiceHub, KEEPING its data disk, backups and encryption key
  .\deploy.ps1 -Cloud <cloud> -Destroy -Purge     remove everything, data included (cannot be undone)
  .\deploy.ps1 -Cloud <cloud> -List               list every resource carrying the ServiceHub tag

  Options: -Yes  -Region R  -Resource X,Y  -Topic T,U (gcp)  -Version V  -ReadOnly
           -Subscription ID (azure)  -SshKey FILE (azure)  -Project ID (gcp)  -Zone Z (gcp)
  The same values can be given as SERVICEHUB_REGION, SERVICEHUB_RESOURCES (comma-separated), SERVICEHUB_VERSION, ...

  This script holds no knowledge of any cloud's resources: Terraform creates and removes everything (see infra\<cloud>\).
  Exit codes: 0 done - 1 failed - 2 this computer is not ready - 3 you said no.

  Works on Windows PowerShell 5.1 and PowerShell 7.
  If scripts are blocked, run it for this one time with:
    powershell -ExecutionPolicy Bypass -File .\deploy.ps1 -Cloud azure
#>
[CmdletBinding()]
param(
  [Parameter(Position = 0)][string]$Cloud = '',
  [switch]$Doctor,
  [switch]$Plan,
  [switch]$Destroy,
  [switch]$Purge,
  [switch]$List,
  [switch]$Yes,
  [switch]$ReadOnly,
  [string]$Region = $env:SERVICEHUB_REGION,
  [string]$Version = $env:SERVICEHUB_VERSION,
  [string[]]$Resource = @(),
  [string[]]$Topic = @(),
  [string]$Subscription = $env:SERVICEHUB_SUBSCRIPTION,
  [string]$SshKey = $env:SERVICEHUB_SSH_KEY,
  [string]$Project = $env:SERVICEHUB_PROJECT,
  [string]$Zone = $env:SERVICEHUB_ZONE
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Say([string]$Text) { Write-Host $Text }
function Note([string]$Text) { Write-Host "Note: $Text" -ForegroundColor Yellow }
function Stop-With([string]$Text, [int]$Code = 1) {
  Write-Host "Stopped: $Text" -ForegroundColor Red
  exit $Code
}

if ($Cloud -notin @('azure', 'aws', 'gcp')) {
  Get-Help $PSCommandPath -Detailed | Out-String | Write-Host
  if ($Cloud -eq '') { exit 0 }
  Stop-With 'Say which cloud: azure, aws or gcp.' 2
}
if ($Purge -and -not $Destroy) { Stop-With '-Purge only goes with -Destroy.' 2 }
if ($env:SERVICEHUB_READ_ONLY -eq '1') { $ReadOnly = $true }

$Dir = Join-Path $PSScriptRoot $Cloud
$TfVars = Join-Path $Dir 'terraform.tfvars'

# Every value that names several things may come as a flag list or as one comma-separated text.
function Split-Names([string[]]$Flag, [string]$FromEnvironment) {
  $all = @()
  foreach ($item in $Flag) { if ($item) { $all += $item.Split(',') } }
  if ($all.Count -eq 0 -and $FromEnvironment) { $all = $FromEnvironment.Split(',') }
  return @($all | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
}

function ConvertTo-HclList([string[]]$Items) {
  if ($null -eq $Items -or $Items.Count -eq 0) { return '[]' }
  $quoted = @()
  foreach ($item in $Items) { $quoted += ('"{0}"' -f $item) }
  return '[' + ($quoted -join ', ') + ']'
}

function Ask([string]$Question, [string]$Default = '') {
  if ($Yes) { return $Default }
  $prompt = $Question
  if ($Default) { $prompt = "$Question [$Default]" }
  $answer = Read-Host $prompt
  if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
  return $answer.Trim()
}

function Confirm-Step([string]$Question) {
  if ($Yes) { return $true }
  $answer = Read-Host "$Question [y/N]"
  return ($answer -match '^(y|yes)$')
}

function Test-Tool([string]$Name) { return [bool](Get-Command $Name -ErrorAction SilentlyContinue) }

# Runs a native command and returns its text, or $null if it failed. Never throws.
function Try-Native([scriptblock]$Command) {
  # A native tool that writes a warning to its error stream must not count as a failure (Windows PowerShell 5.1 would stop).
  $ErrorActionPreference = 'Continue'
  try {
    $text = & $Command 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return ($text | Out-String).Trim()
  } catch { return $null }
}

function Invoke-Terraform {
  & terraform "-chdir=$Dir" @args
  return $LASTEXITCODE
}

# -- 1. Is this computer ready? ---------------------------------------------------------------------
function Test-Ready {
  $ok = $true
  if (-not (Test-Tool 'terraform')) {
    Say 'X Terraform is not installed.   https://developer.hashicorp.com/terraform/install'; $ok = $false
  } else {
    Say ('OK Terraform ' + ((& terraform version | Select-Object -First 1) -replace '^Terraform ', ''))
  }
  switch ($Cloud) {
    'azure' {
      if (-not (Test-Tool 'az')) { Say 'X The Azure CLI is not installed.   https://learn.microsoft.com/cli/azure/install-azure-cli'; $ok = $false }
      else {
        $account = Try-Native { az account show --query '[name,id]' -o tsv }
        if (-not $account) { Say 'X Not signed in to Azure.   Run: az login'; $ok = $false }
        else { Say ('OK Azure subscription: ' + ($account -replace '\s+', ' ')) }
      }
    }
    'aws' {
      if (-not (Test-Tool 'aws')) { Say 'X The AWS CLI is not installed.   https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html'; $ok = $false }
      else {
        $account = Try-Native { aws sts get-caller-identity --query '[Account,Arn]' --output text }
        if (-not $account) { Say 'X Not signed in to AWS.   Run: aws configure   (or: aws sso login)'; $ok = $false }
        else { Say ('OK AWS account: ' + ($account -replace '\s+', ' ')) }
      }
      if (-not (Test-Tool 'session-manager-plugin')) {
        Note 'the Session Manager plugin is not installed. You need it to open ServiceHub afterwards: https://docs.aws.amazon.com/systems-manager/latest/userguide/session-manager-working-with-install-plugin.html'
      }
    }
    'gcp' {
      if (-not (Test-Tool 'gcloud')) { Say 'X The Google Cloud CLI is not installed.   https://cloud.google.com/sdk/docs/install'; $ok = $false }
      else {
        $account = Try-Native { gcloud auth list --filter=status:ACTIVE '--format=value(account)' }
        if (-not $account) { Say 'X Not signed in to Google Cloud.   Run: gcloud auth login   and   gcloud auth application-default login'; $ok = $false }
        else {
          Say ('OK Google account: ' + ($account -split "`n")[0])
          if (-not (Try-Native { gcloud auth application-default print-access-token })) {
            Say 'X Terraform has no Google credentials.   Run: gcloud auth application-default login'; $ok = $false
          }
        }
      }
    }
  }
  if (-not $ok) { Stop-With 'This computer is not ready yet - fix the lines marked X and run it again.' 2 }
}

# -- 2. The answers, written once to terraform.tfvars (names only - never a secret) -----------------
function Write-Answers {
  if (Test-Path $TfVars) {
    Say "Using the answers already in $Cloud\terraform.tfvars (edit or delete that file to change them)."
    return
  }
  $lines = @('# Written by deploy.ps1. Names only - no secret belongs in this file.')
  $resources = Split-Names $Resource $env:SERVICEHUB_RESOURCES
  $hint = ''
  switch ($Cloud) {
    'azure' {
      $sub = $Subscription
      if (-not $sub) { $sub = Ask 'Azure subscription ID' (Try-Native { az account show --query id -o tsv }) }
      $reg = $Region
      if (-not $reg) { $reg = Ask 'Azure region (for example westeurope)' }
      $keyFile = $SshKey
      if (-not $keyFile) {
        $found = ''
        foreach ($candidate in @((Join-Path $HOME '.ssh\id_ed25519.pub'), (Join-Path $HOME '.ssh\id_rsa.pub'))) {
          if (Test-Path $candidate) { $found = $candidate; break }
        }
        $keyFile = Ask 'Your SSH PUBLIC key file' $found
      }
      if (-not $keyFile -or -not (Test-Path $keyFile)) { Stop-With "There is no file '$keyFile'. Make a key with: ssh-keygen -t ed25519" 2 }
      if ($keyFile -notlike '*.pub') { Stop-With "'$keyFile' does not look like a PUBLIC key (it should end in .pub)." 2 }
      $ip = Ask 'Your own public IP address - the only one allowed to open the tunnel (see https://api.ipify.org)' $env:SERVICEHUB_MY_IP
      if (-not $sub -or -not $reg -or -not $ip) { Stop-With 'The subscription, the region and your IP address are all needed.' 2 }
      $key = (Get-Content -Raw $keyFile).Trim()
      $lines += ('subscription_id  = "{0}"' -f $sub)
      $lines += ('region           = "{0}"' -f $reg)
      $lines += ('ssh_public_key   = "{0}"' -f $key)
      $lines += ('allowed_ssh_cidr = "{0}/32"' -f ($ip -replace '/32$', ''))
      $hint = 'Service Bus namespace resource IDs to note on the VM, comma-separated (optional)'
    }
    'aws' {
      $reg = $Region
      if (-not $reg) { $reg = Ask 'AWS region (for example eu-west-1)' (Try-Native { aws configure get region }) }
      if (-not $reg) { Stop-With 'The region is needed.' 2 }
      $lines += ('region = "{0}"' -f $reg)
      $hint = 'ARNs of the queues ServiceHub may use - each queue AND its dead-letter queue, comma-separated'
    }
    'gcp' {
      $proj = $Project
      if (-not $proj) { $proj = Ask 'Google Cloud project ID' (Try-Native { gcloud config get-value project }) }
      $reg = $Region
      if (-not $reg) { $reg = Ask 'Region (for example europe-west1)' }
      $zn = $Zone
      if (-not $zn) {
        $guess = ''
        if ($reg) { $guess = "$reg-b" }
        $zn = Ask 'Zone' $guess
      }
      if (-not $proj -or -not $reg -or -not $zn) { Stop-With 'The project, the region and the zone are all needed.' 2 }
      $topics = Split-Names $Topic $env:SERVICEHUB_TOPICS
      if ($topics.Count -eq 0) { $topics = Split-Names @((Ask 'Topics a replayed message is published back to, comma-separated (optional)')) '' }
      $lines += ('project_id = "{0}"' -f $proj)
      $lines += ('region     = "{0}"' -f $reg)
      $lines += ('zone       = "{0}"' -f $zn)
      $lines += ('replay_topics = ' + (ConvertTo-HclList $topics))
      $hint = 'Subscriptions ServiceHub may read - each subscription AND its dead-letter subscription, comma-separated'
    }
  }
  if ($resources.Count -eq 0) { $resources = Split-Names @((Ask $hint)) '' }
  $lines += ('messaging_resources = ' + (ConvertTo-HclList $resources))
  if ($ReadOnly) { $lines += 'allow_replay = false' }
  if ($Version) { $lines += ('servicehub_version = "{0}"' -f $Version) }
  # Plain UTF-8 without a byte-order mark: Windows PowerShell's default would write one, which Terraform rejects.
  [System.IO.File]::WriteAllLines($TfVars, $lines, (New-Object System.Text.UTF8Encoding($false)))
  Say "Wrote $Cloud\terraform.tfvars"
}

# One output value; a list comes back as an array of texts.
function Get-Output([string]$Name) {
  $json = Try-Native { terraform "-chdir=$Dir" output -json $Name }
  if (-not $json) { return @() }
  return @($json | ConvertFrom-Json)
}

function Show-Tagged([string]$Command = '') {
  if (-not $Command) { $Command = [string](Get-Output 'list_command' | Select-Object -First 1) }
  if (-not $Command) { Say 'Nothing is deployed from this folder, so there is no account to look in.'; return }
  Say 'Everything carrying the ServiceHub tag:'
  try { Invoke-Expression $Command } catch { Note "the cloud's CLI could not list resources - run it yourself: $Command" }
}

function Get-TfVar([string]$Name) {
  $match = Select-String -Path $TfVars -Pattern ('^{0}\s*=\s*"(.*)"' -f $Name) | Select-Object -First 1
  if ($match) { return $match.Matches[0].Groups[1].Value }
  return ''
}

# -- 3. Actions ------------------------------------------------------------------------------------
Test-Ready
if ($Doctor) { Say 'This computer is ready.'; exit 0 }

if ($List) {
  [void](Invoke-Terraform init -input=false)
  Show-Tagged
  exit 0
}

if ($Destroy) {
  if (-not (Test-Path $TfVars)) { Stop-With "There is no $Cloud\terraform.tfvars, so nothing was deployed from this folder." 1 }
  [void](Invoke-Terraform init -input=false)
  $listCommand = [string](Get-Output 'list_command' | Select-Object -First 1)
  if ($Purge) {
    Say "This removes EVERYTHING, including ServiceHub's data, its backups and its encryption key. It cannot be undone."
    if (-not $Yes) {
      $typed = Read-Host 'Type PURGE to go on'
      if ($typed -cne 'PURGE') { Stop-With 'Nothing was removed.' 3 }
    }
  } else {
    Say 'This removes ServiceHub but KEEPS:'
    foreach ($kept in (Get-Output 'kept_on_destroy')) { Say "  - $kept" }
    Say 'Run it again with -Purge to remove those too.'
    if (-not (Confirm-Step "Remove ServiceHub from $Cloud?")) { Stop-With 'Nothing was removed.' 3 }
    # What is kept is taken out of Terraform's hands, so the destroy below cannot touch it.
    foreach ($address in (Get-Output 'kept_addresses')) {
      [void](Try-Native { terraform "-chdir=$Dir" state rm $address })
    }
  }
  & terraform "-chdir=$Dir" destroy -input=false -auto-approve
  if ($LASTEXITCODE -ne 0) { Stop-With 'The destroy did not finish. It is safe to run the same command again.' 1 }
  if ($Purge) {
    # Snapshots are made by the cloud's own schedule, not by Terraform, so they are removed here.
    if ($Cloud -eq 'aws') {
      $reg = Get-TfVar 'region'
      $snaps = Try-Native { aws ec2 describe-snapshots --owner-ids self --region $reg --filters 'Name=tag:app,Values=servicehub' --query 'Snapshots[].SnapshotId' --output text }
      if ($snaps) { foreach ($snap in ($snaps -split '\s+')) { if ($snap) { & aws ec2 delete-snapshot --region $reg --snapshot-id $snap | Out-Null } } }
    }
    if ($Cloud -eq 'gcp') {
      $proj = Get-TfVar 'project_id'
      $snaps = Try-Native { gcloud compute snapshots list --project $proj '--filter=labels.app=servicehub' '--format=value(name)' }
      if ($snaps) { foreach ($snap in ($snaps -split '\s+')) { if ($snap) { & gcloud compute snapshots delete $snap --project $proj --quiet | Out-Null } } }
    }
    Remove-Item -Force $TfVars
  }
  Say ''
  Show-Tagged $listCommand
  if ($Purge) { Say 'If the list above is empty, nothing of ServiceHub is left.' }
  exit 0
}

Write-Answers
$planFile = Join-Path $Dir 'servicehub.tfplan'
& terraform "-chdir=$Dir" init -input=false | Out-Null
if ($LASTEXITCODE -ne 0) { Stop-With "Terraform could not start in infra\$Cloud." 1 }
& terraform "-chdir=$Dir" plan -input=false -out=servicehub.tfplan
if ($LASTEXITCODE -ne 0) { Stop-With 'Terraform could not work out a plan - the message above says why.' 1 }
if ($Plan) {
  Remove-Item -Force $planFile -ErrorAction SilentlyContinue
  Say 'Nothing was changed.'
  exit 0
}

Say ''
Say "The plan above lists everything that will be created. What it costs is in infra\$Cloud\README.md."
if (-not (Confirm-Step "Create ServiceHub in $Cloud now?")) {
  Remove-Item -Force $planFile -ErrorAction SilentlyContinue
  Stop-With 'Nothing was created.' 3
}
& terraform "-chdir=$Dir" apply -input=false servicehub.tfplan
$applied = $LASTEXITCODE
Remove-Item -Force $planFile -ErrorAction SilentlyContinue
if ($applied -ne 0) { Stop-With 'It did not finish. Run the same command again to carry on, or add -Destroy to remove what was made.' 1 }
Say ''
& terraform "-chdir=$Dir" output -raw next_steps
