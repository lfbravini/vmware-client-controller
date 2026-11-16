#Requires -Version 5.1
<#
.SYNOPSIS
  Non-interactive UC1–UC5 (+ UC6–UC8 when available) against Portainer edge.
#>
[CmdletBinding()]
param(
  [string] $EdgeUrl,
  [switch] $SkipHang
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "LabCommon.ps1")
$cfg = Get-LabConfig -EdgeUrl $EdgeUrl
Ensure-LabLogDir $cfg

$results = [ordered]@{}
function Set-Uc([string]$id, [bool]$pass, [string]$detail) {
  $results[$id] = @{ pass = $pass; detail = $detail }
  $mark = if ($pass) { "PASS" } else { "FAIL" }
  Write-Host "[$mark] $id — $detail"
}

Write-Host "=== lab-test edge=$($cfg.EdgeUrl) ==="

# UC5 console
try {
  $ui = Invoke-WebRequest -Uri "$($cfg.EdgeUrl)/" -UseBasicParsing -TimeoutSec 15
  $tenants = @(Invoke-LabAdmin -Config $cfg -Path "/api/v1/admin/tenants")
  Set-Uc "UC5" ($ui.Content -match "Horizon Fleet" -and $tenants.Count -ge 1) "ui=$($ui.StatusCode) tenants=$($tenants.Count)"
} catch {
  Set-Uc "UC5" $false $_.Exception.Message
}

# UC1 enroll
try {
  $eps = @(Invoke-LabAdmin -Config $cfg -Path "/api/v1/admin/endpoints")
  Set-Uc "UC1" ($eps.Count -ge 1) "endpoints=$($eps.Count) host=$(@($eps)[0].hostname)"
} catch {
  Set-Uc "UC1" $false $_.Exception.Message
}

# Ensure FakeClient live for UC2/UC3
$fakeExe = Join-Path $cfg.Root "src\HorizonWatchdog.FakeClient\bin\Release\net8.0-windows\HorizonWatchdog.FakeClient.exe"
if (-not (Get-Process -Name HorizonWatchdog.FakeClient -ErrorAction SilentlyContinue)) {
  if (Test-Path $fakeExe) { Start-Process $fakeExe; Start-Sleep 10 }
}

function Get-LiveInstance {
  $fp = Get-Process -Name HorizonWatchdog.FakeClient -ErrorAction SilentlyContinue | Select-Object -First 1
  if (-not $fp) { return $null }
  $inst = @(Invoke-LabAdmin -Config $cfg -Path "/api/v1/admin/instances")
  foreach ($row in $inst) {
    if (($row.rootPid -as [int]) -eq $fp.Id) {
      return [pscustomobject]@{ Row = $row; Pid = $fp.Id }
    }
  }
  return $null
}

$live = $null
for ($i = 1; $i -le 18; $i++) {
  $live = Get-LiveInstance
  if ($live) { break }
  Start-Sleep 5
  if ($i -eq 6) {
    Stop-Process -Name HorizonWatchdog.Tray -Force -ErrorAction SilentlyContinue
    Start-Sleep 1
    $tray = "${env:ProgramFiles}\HorizonWatchdog\HorizonWatchdog.Tray.exe"
    if (-not (Test-Path $tray)) {
      $tray = Join-Path $cfg.Root "src\HorizonWatchdog.Tray\bin\Release\net8.0-windows\HorizonWatchdog.Tray.exe"
    }
    if (Test-Path $tray) { Start-Process $tray }
  }
}

if ($live) {
  Set-Uc "UC2" $true "pid=$($live.Pid) instance=$($live.Row.instanceId) job=$($live.Row.jobObjectName)"
} else {
  Set-Uc "UC2" $false "No live FakeClient instance in admin API"
}

# UC3 admin restart
if ($live) {
  try {
    $id = [Guid]::Parse([string]$live.Row.instanceId)
    $pidBefore = [int]$live.Pid
    Invoke-LabAdmin -Config $cfg -Path "/api/v1/admin/instances/restart" -Method POST -Body @{
      instanceId = $id.ToString()
      ttlSec = 120
    } | Out-Null
    $dead = $false
    for ($i = 1; $i -le 20; $i++) {
      if (-not (Get-Process -Id $pidBefore -ErrorAction SilentlyContinue)) { $dead = $true; break }
      Start-Sleep 1
    }
    Set-Uc "UC3" $dead "pid $pidBefore terminated=$dead"
  } catch {
    Set-Uc "UC3" $false $_.Exception.Message
  }
} else {
  Set-Uc "UC3" $false "skipped — no live instance"
}

# UC4 hang heal
if (-not $SkipHang) {
  try {
    Stop-Process -Name HorizonWatchdog.FakeClient -Force -ErrorAction SilentlyContinue
    Start-Sleep 2
    $p = Start-Process $fakeExe -PassThru
    $synced = $null
    for ($i = 1; $i -le 18; $i++) {
      Start-Sleep 5
      $synced = Get-LiveInstance
      if ($synced -and $synced.Pid -eq $p.Id) { break }
      $synced = $null
    }
    if (-not $synced) { throw "FakeClient not synced before hang" }

    Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class LabHangClick {
  public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr hWndParent, EnumProc lpEnumFunc, IntPtr lParam);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
  public const uint BM_CLICK = 0x00F5;
  public static IntPtr FindTop(uint pid) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid) { found = h; return false; } return true; }, IntPtr.Zero);
    return found;
  }
  public static IntPtr FindButton(IntPtr parent) {
    IntPtr found = IntPtr.Zero;
    EnumChildWindows(parent, (h, l) => {
      var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
      if (sb.ToString().IndexOf("Simulate UI hang", StringComparison.OrdinalIgnoreCase) >= 0) { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
"@
    $top = [LabHangClick]::FindTop([uint32]$p.Id)
    $btn = [LabHangClick]::FindButton($top)
    if ($btn -eq [IntPtr]::Zero) { throw "Hang button not found" }
    [void][LabHangClick]::PostMessage($btn, [LabHangClick]::BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero)
    $healed = $false
    for ($i = 1; $i -le 45; $i++) {
      if (-not (Get-Process -Id $p.Id -ErrorAction SilentlyContinue)) { $healed = $true; break }
      Start-Sleep 2
    }
    Set-Uc "UC4" $healed "hang auto-heal=$healed (~$($i*2)s)"
  } catch {
    Set-Uc "UC4" $false $_.Exception.Message
  }
} else {
  Set-Uc "UC4" $true "skipped"
}

# UC6 command status endpoint (optional)
try {
  $cmds = Invoke-LabAdmin -Config $cfg -Path "/api/v1/admin/commands?take=5"
  Set-Uc "UC6" $true "commands endpoint ok count=$(@($cmds).Count)"
} catch {
  Set-Uc "UC6" $false "not available yet: $($_.Exception.Message)"
}

$out = [pscustomobject]@{
  at = (Get-Date).ToString("o")
  edge = $cfg.EdgeUrl
  results = $results
}
$outPath = Join-Path $cfg.LogDir "last-test.json"
$out | ConvertTo-Json -Depth 6 | Set-Content $outPath -Encoding UTF8
Write-Host "Wrote $outPath"

$required = @("UC1","UC2","UC3","UC4","UC5")
$failed = @($required | Where-Object { -not $results[$_].pass })
if ($failed.Count -gt 0) {
  throw "lab-test FAILED: $($failed -join ', ')"
}
Write-Host "lab-test ALL REQUIRED PASS"
