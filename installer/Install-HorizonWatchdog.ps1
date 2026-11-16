#Requires -RunAsAdministrator
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string] $ServerUrl,
  [Parameter(Mandatory = $true)]
  [string] $EnrollmentToken,
  [string] $SourceRoot = (Split-Path $PSScriptRoot -Parent),
  [string] $ZipPath,
  [string] $InstallDir = "${env:ProgramFiles}\HorizonWatchdog"
)

$ErrorActionPreference = "Stop"
$ServerUrl = $ServerUrl.TrimEnd('/')
$serviceName = "HorizonWatchdog"

Write-Host "Stopping existing service/processes..."
& sc.exe stop $serviceName 2>$null | Out-Null
Start-Sleep -Seconds 2
Stop-Process -Name "HorizonWatchdog.Service","HorizonWatchdog.Tray" -Force -ErrorAction SilentlyContinue
& sc.exe delete $serviceName 2>$null | Out-Null
Start-Sleep -Seconds 1

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
if ($ZipPath) {
  if (-not (Test-Path $ZipPath)) { throw "Zip not found: $ZipPath" }
  $stage = Join-Path $env:TEMP "horizon-watchdog-zip"
  if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
  Expand-Archive -Path $ZipPath -DestinationPath $stage -Force
  Copy-Item (Join-Path $stage "*") $InstallDir -Recurse -Force
} else {
  $svcProject = Join-Path $SourceRoot "src\HorizonWatchdog.Service\bin\Release\net8.0-windows"
  $trayProject = Join-Path $SourceRoot "src\HorizonWatchdog.Tray\bin\Release\net8.0-windows"
  if (-not (Test-Path (Join-Path $svcProject "HorizonWatchdog.Service.exe"))) {
    throw "Missing Service binaries at $svcProject"
  }
  if (-not (Test-Path (Join-Path $trayProject "HorizonWatchdog.Tray.exe"))) {
    throw "Missing Tray binaries at $trayProject"
  }
  Copy-Item -Path (Join-Path $svcProject "*") -Destination $InstallDir -Recurse -Force
  Copy-Item -Path (Join-Path $trayProject "*") -Destination $InstallDir -Recurse -Force
}

$svcExe = Join-Path $InstallDir "HorizonWatchdog.Service.exe"
$trayExe = Join-Path $InstallDir "HorizonWatchdog.Tray.exe"
if (-not (Test-Path $svcExe)) { throw "Service exe missing: $svcExe" }
if (-not (Test-Path $trayExe)) { throw "Tray exe missing: $trayExe" }

$regPath = "HKLM:\SOFTWARE\HorizonWatchdog"
New-Item -Path $regPath -Force | Out-Null
Set-ItemProperty -Path $regPath -Name "ServerUrl" -Value $ServerUrl
Remove-ItemProperty -Path $regPath -Name "AgentSecretProtected" -ErrorAction SilentlyContinue

$binPath = "`"$svcExe`""
Write-Host "Creating service $serviceName"
& sc.exe create $serviceName binPath= $binPath start= auto obj= LocalSystem DisplayName= "Horizon Watchdog Service" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc create failed with $LASTEXITCODE" }

$svcReg = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
$envLines = @(
  "HORIZON_SERVER=$ServerUrl",
  "HORIZON_ENROLLMENT_TOKEN=$EnrollmentToken"
)
New-ItemProperty -Path $svcReg -Name "Environment" -PropertyType MultiString -Value $envLines -Force | Out-Null

Write-Host "Starting service..."
& sc.exe start $serviceName | Out-Null
Start-Sleep -Seconds 3
$svc = Get-Service -Name $serviceName -ErrorAction Stop
Write-Host "Service status: $($svc.Status)"

# Machine-wide Tray at interactive logon
$taskName = "HorizonWatchdogTray"
schtasks /Delete /TN $taskName /F 2>$null | Out-Null
$action = "schtasks /Create /TN `"$taskName`" /SC ONLOGON /RL LIMITED /RU `"Interactive`" /TR `"\`"$trayExe\`"`" /F"
# Fallback: use current user + AtLogon for all users via /RU INTERACTIVE may fail; use HKLM Run for all users
$runAll = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
Set-ItemProperty -Path $runAll -Name "HorizonWatchdogTray" -Value "`"$trayExe`""
Set-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "HorizonWatchdogTray" -Value "`"$trayExe`""
Start-Process -FilePath $trayExe -WorkingDirectory $InstallDir
Write-Host "Tray registered (HKLM+HKCU Run) and started."

Write-Host "Install complete. ServerUrl=$ServerUrl InstallDir=$InstallDir"
# Note: after successful enroll, Service keeps token in env for re-enroll on secret loss; operators can clear Environment later.
