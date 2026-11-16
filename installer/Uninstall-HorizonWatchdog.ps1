#Requires -RunAsAdministrator
[CmdletBinding()]
param(
  [string] $InstallDir = "${env:ProgramFiles}\HorizonWatchdog"
)

$ErrorActionPreference = "Continue"
$serviceName = "HorizonWatchdog"

Write-Host "Stopping service/processes..."
& sc.exe stop $serviceName 2>$null | Out-Null
Start-Sleep -Seconds 2
Stop-Process -Name "HorizonWatchdog.Service","HorizonWatchdog.Tray" -Force -ErrorAction SilentlyContinue
& sc.exe delete $serviceName 2>$null | Out-Null

Remove-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "HorizonWatchdogTray" -ErrorAction SilentlyContinue

if (Test-Path $InstallDir) {
  Start-Sleep -Seconds 1
  Remove-Item -Path $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
  Write-Host "Removed $InstallDir"
}

Write-Host "Uninstall complete. HKLM\SOFTWARE\HorizonWatchdog left intact (agent identity). Remove manually if desired."
