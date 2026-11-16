#Requires -Version 5.1
<#
.SYNOPSIS
  Reset Service/Tray/FakeClient against Portainer edge. Uses installer when elevated path available.
#>
[CmdletBinding()]
param(
  [string] $EdgeUrl,
  [string] $EnrollmentToken,
  [switch] $FromZip,
  [string] $ZipPath
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "LabCommon.ps1")
$cfg = Get-LabConfig -EdgeUrl $EdgeUrl -EnrollToken $EnrollmentToken
Ensure-LabLogDir $cfg

Write-Host "=== lab-agents edge=$($cfg.EdgeUrl) ==="
Stop-Process -Name "HorizonWatchdog.FakeClient","HorizonWatchdog.Tray" -Force -ErrorAction SilentlyContinue

$installScript = Join-Path $cfg.Root "installer\Install-HorizonWatchdog.ps1"
$elev = @"
`$ErrorActionPreference = 'Stop'
Stop-Process -Name 'HorizonWatchdog.FakeClient','HorizonWatchdog.Tray' -Force -ErrorAction SilentlyContinue
if (Get-Service -Name 'HorizonWatchdog' -ErrorAction SilentlyContinue) {
  & sc.exe stop HorizonWatchdog | Out-Null
  Start-Sleep 2
}
"@

if ($FromZip) {
  if ([string]::IsNullOrWhiteSpace($ZipPath)) {
    $ZipPath = Join-Path $cfg.Root "artifacts\HorizonWatchdog-win-x64.zip"
  }
  $elev += @"

& '$installScript' -ServerUrl '$($cfg.EdgeUrl)' -EnrollmentToken '$($cfg.EnrollToken)' -ZipPath '$ZipPath'
"@
} else {
  $elev += @"

& '$installScript' -ServerUrl '$($cfg.EdgeUrl)' -EnrollmentToken '$($cfg.EnrollToken)' -SourceRoot '$($cfg.Root)'
"@
}

$elev += @"

Set-Content -Path '$($cfg.LogDir)\agents-ready.txt' -Value ((Get-Date).ToString('o'))
"@

$enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($elev))
Write-Host "Requesting elevation for Service install (approve UAC if prompted)..."
$p = Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $enc" -PassThru -Wait
if ($p.ExitCode -ne 0 -and $p.ExitCode -ne $null) {
  Write-Host "Elevated process exit=$($p.ExitCode)"
}

$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline) {
  if (Test-Path (Join-Path $cfg.LogDir "agents-ready.txt")) { break }
  Start-Sleep 2
}
if (-not (Test-Path (Join-Path $cfg.LogDir "agents-ready.txt"))) {
  # Fallback: start tray from Release if service already running
  Write-Host "No agents-ready marker; trying Tray from Release + existing service..."
  $svc = Get-Service HorizonWatchdog -ErrorAction SilentlyContinue
  if (-not $svc -or $svc.Status -ne "Running") {
    throw "HorizonWatchdog service not running. Approve UAC and re-run lab-agents.ps1"
  }
  $tray = Join-Path $cfg.Root "src\HorizonWatchdog.Tray\bin\Release\net8.0-windows\HorizonWatchdog.Tray.exe"
  if (Test-Path "${env:ProgramFiles}\HorizonWatchdog\HorizonWatchdog.Tray.exe") {
    $tray = "${env:ProgramFiles}\HorizonWatchdog\HorizonWatchdog.Tray.exe"
  }
  Start-Process $tray
}

# Ensure FakeClient
Stop-Process -Name HorizonWatchdog.FakeClient -Force -ErrorAction SilentlyContinue
Start-Sleep 1
$fake = Join-Path $cfg.Root "src\HorizonWatchdog.FakeClient\bin\Release\net8.0-windows\HorizonWatchdog.FakeClient.exe"
if (-not (Test-Path $fake)) { throw "Missing FakeClient at $fake" }
Start-Process $fake

# Wait UC1/UC2
$ok = $false
for ($i = 1; $i -le 24; $i++) {
  Start-Sleep 5
  try {
    $eps = @(Invoke-LabAdmin -Config $cfg -Path "/api/v1/admin/endpoints")
    $inst = @(Invoke-LabAdmin -Config $cfg -Path "/api/v1/admin/instances")
    $fakeProc = Get-Process -Name HorizonWatchdog.FakeClient -ErrorAction SilentlyContinue | Select-Object -First 1
    $live = $null
    if ($fakeProc) {
      foreach ($row in $inst) {
        if (($row.rootPid -as [int]) -eq $fakeProc.Id) { $live = $row; break }
      }
    }
    Write-Host "wait $i endpoints=$(@($eps).Count) liveInstance=$($null -ne $live)"
    if (@($eps).Count -ge 1 -and $live) { $ok = $true; break }
    if ($i -eq 8 -and -not $live) {
      # bounce tray
      Stop-Process -Name HorizonWatchdog.Tray -Force -ErrorAction SilentlyContinue
      Start-Sleep 1
      $tray = "${env:ProgramFiles}\HorizonWatchdog\HorizonWatchdog.Tray.exe"
      if (-not (Test-Path $tray)) {
        $tray = Join-Path $cfg.Root "src\HorizonWatchdog.Tray\bin\Release\net8.0-windows\HorizonWatchdog.Tray.exe"
      }
      Start-Process $tray
    }
  } catch {
    Write-Host "wait $i err: $($_.Exception.Message)"
  }
}
if (-not $ok) { throw "lab-agents: UC1/UC2 not ready" }
Write-Host "lab-agents OK"
