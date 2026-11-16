#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Install HorizonWatchdog Service (LocalSystem) + Tray (logon) for V1.
.PARAMETER ServerUrl
  Control plane edge URL, e.g. http://172.16.100.81:18080
.PARAMETER EnrollmentToken
  Enrollment token plaintext (lab default documented in LAB_SETUP.md)
.PARAMETER SourceRoot
  Repo root containing Release binaries (default: parent of installer/)
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string] $ServerUrl,
  [Parameter(Mandatory = $true)]
  [string] $EnrollmentToken,
  [string] $SourceRoot = (Split-Path $PSScriptRoot -Parent),
  [string] $InstallDir = "${env:ProgramFiles}\HorizonWatchdog"
)

$ErrorActionPreference = "Stop"
$ServerUrl = $ServerUrl.TrimEnd('/')
$serviceName = "HorizonWatchdog"
$svcProject = Join-Path $SourceRoot "src\HorizonWatchdog.Service\bin\Release\net8.0-windows"
$trayProject = Join-Path $SourceRoot "src\HorizonWatchdog.Tray\bin\Release\net8.0-windows"

if (-not (Test-Path (Join-Path $svcProject "HorizonWatchdog.Service.exe"))) {
  throw "Missing Service binaries at $svcProject. Run: dotnet build HorizonFleetControl.sln -c Release"
}
if (-not (Test-Path (Join-Path $trayProject "HorizonWatchdog.Tray.exe"))) {
  throw "Missing Tray binaries at $trayProject. Run: dotnet build HorizonFleetControl.sln -c Release"
}

Write-Host "Stopping existing service/processes..."
& sc.exe stop $serviceName 2>$null | Out-Null
Start-Sleep -Seconds 2
Stop-Process -Name "HorizonWatchdog.Service","HorizonWatchdog.Tray" -Force -ErrorAction SilentlyContinue
& sc.exe delete $serviceName 2>$null | Out-Null
Start-Sleep -Seconds 1

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Write-Host "Copying binaries to $InstallDir"
Copy-Item -Path (Join-Path $svcProject "*") -Destination $InstallDir -Recurse -Force
Copy-Item -Path (Join-Path $trayProject "*") -Destination $InstallDir -Recurse -Force

$svcExe = Join-Path $InstallDir "HorizonWatchdog.Service.exe"
$trayExe = Join-Path $InstallDir "HorizonWatchdog.Tray.exe"
if (-not (Test-Path $svcExe)) { throw "Service exe missing after copy: $svcExe" }
if (-not (Test-Path $trayExe)) { throw "Tray exe missing after copy: $trayExe" }

$regPath = "HKLM:\SOFTWARE\HorizonWatchdog"
New-Item -Path $regPath -Force | Out-Null
Set-ItemProperty -Path $regPath -Name "ServerUrl" -Value $ServerUrl
# Force re-enroll against this control plane
Remove-ItemProperty -Path $regPath -Name "AgentSecretProtected" -ErrorAction SilentlyContinue

$binPath = "`"$svcExe`""
Write-Host "Creating service $serviceName"
& sc.exe create $serviceName binPath= $binPath start= auto obj= LocalSystem DisplayName= "Horizon Watchdog Service" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc create failed with $LASTEXITCODE" }

# Service-specific environment (HORIZON_*)
$svcReg = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
$envLines = @(
  "HORIZON_SERVER=$ServerUrl",
  "HORIZON_ENROLLMENT_TOKEN=$EnrollmentToken"
)
New-ItemProperty -Path $svcReg -Name "Environment" -PropertyType MultiString -Value $envLines -Force | Out-Null

Write-Host "Starting service..."
& sc.exe start $serviceName | Out-Null
Start-Sleep -Seconds 2
$svc = Get-Service -Name $serviceName -ErrorAction Stop
Write-Host "Service status: $($svc.Status)"

# Tray at interactive logon (current user Run key + start now)
$runKey = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
Set-ItemProperty -Path $runKey -Name "HorizonWatchdogTray" -Value "`"$trayExe`""
Start-Process -FilePath $trayExe -WorkingDirectory $InstallDir
Write-Host "Tray registered for logon and started."

Write-Host "Install complete. ServerUrl=$ServerUrl InstallDir=$InstallDir"
