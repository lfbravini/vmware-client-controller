#Requires -Version 5.1
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "artifacts\agent-publish"
$zip = Join-Path $root "artifacts\HorizonWatchdog-win-x64.zip"
New-Item -ItemType Directory -Force -Path (Split-Path $zip) | Out-Null
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $out | Out-Null

dotnet publish (Join-Path $root "src\HorizonWatchdog.Service\HorizonWatchdog.Service.csproj") -c Release -o (Join-Path $out "Service") --nologo
dotnet publish (Join-Path $root "src\HorizonWatchdog.Tray\HorizonWatchdog.Tray.csproj") -c Release -o (Join-Path $out "Tray") --nologo
dotnet publish (Join-Path $root "src\HorizonWatchdog.FakeClient\HorizonWatchdog.FakeClient.csproj") -c Release -o (Join-Path $out "FakeClient") --nologo

# Flat layout for installer
$flat = Join-Path $root "artifacts\HorizonWatchdog-win-x64"
if (Test-Path $flat) { Remove-Item $flat -Recurse -Force }
New-Item -ItemType Directory -Force -Path $flat | Out-Null
Copy-Item (Join-Path $out "Service\*") $flat -Recurse -Force
Copy-Item (Join-Path $out "Tray\*") $flat -Recurse -Force
Copy-Item (Join-Path $out "FakeClient\HorizonWatchdog.FakeClient.exe") $flat -Force -ErrorAction SilentlyContinue

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $flat "*") -DestinationPath $zip -Force
Write-Host "Published $zip"
