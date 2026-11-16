#Requires -Version 5.1
<#
.SYNOPSIS
  Build Release + web, push images to Portainer host, recreate horizon-fleet Git stack.
#>
[CmdletBinding()]
param(
  [string] $EdgeUrl,
  [switch] $SkipBuild,
  [switch] $SkipImages,
  [switch] $SkipStack
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "LabCommon.ps1")
$cfg = Get-LabConfig -EdgeUrl $EdgeUrl
Enable-LabTlsBypass
Ensure-LabLogDir $cfg
$headers = Get-PortainerHeaders $cfg
$docker = "$($cfg.PortainerUrl)/api/endpoints/$($cfg.EndpointId)/docker"
$gitSha = (git -C $cfg.Root rev-parse --short HEAD 2>$null)
if (-not $gitSha) { $gitSha = "local" }
$apiTag = "horizon-control-api:lab"
$webTag = "horizon-control-web:lab"
$apiVersionTag = "horizon-control-api:lab-$gitSha"
$webVersionTag = "horizon-control-web:lab-$gitSha"

Write-Host "=== lab-redeploy edge=$($cfg.EdgeUrl) sha=$gitSha ==="

if (-not $SkipBuild) {
  Write-Host "Building .NET Release..."
  dotnet build (Join-Path $cfg.Root "HorizonFleetControl.sln") -c Release --nologo
  if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }
  Write-Host "Building web..."
  Push-Location (Join-Path $cfg.Root "src\HorizonControl.Web")
  try {
    if (-not (Test-Path "node_modules")) { npm install --no-fund --no-audit }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm build failed" }
  } finally { Pop-Location }
}

function Invoke-DockerPull([string]$image) {
  Write-Host "Pull $image"
  $uri = "$docker/images/create?fromImage=$([uri]::EscapeDataString($image))"
  $req = [System.Net.HttpWebRequest]::Create($uri)
  $req.Method = "POST"; $req.ContentLength = 0
  $req.Headers.Add("X-API-Key", $cfg.PortainerToken)
  $req.Timeout = 900000; $req.ReadWriteTimeout = 900000
  $resp = $req.GetResponse()
  $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
  while ($null -ne ($line = $reader.ReadLine())) { }
}

function Invoke-DockerBuild([string]$tag, [string]$tarPath) {
  Write-Host "Build $tag"
  $uri = "$docker/build?t=$([uri]::EscapeDataString($tag))&dockerfile=Dockerfile&q=false"
  $bytes = [IO.File]::ReadAllBytes($tarPath)
  $req = [System.Net.HttpWebRequest]::Create($uri)
  $req.Method = "POST"
  $req.ContentType = "application/x-tar"
  $req.Headers.Add("X-API-Key", $cfg.PortainerToken)
  $req.ContentLength = $bytes.Length
  $req.Timeout = 900000; $req.ReadWriteTimeout = 900000
  $s = $req.GetRequestStream(); $s.Write($bytes, 0, $bytes.Length); $s.Close()
  $resp = $req.GetResponse()
  $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
  $err = $false
  while ($null -ne ($line = $reader.ReadLine())) {
    if ($line -match '"error"') { Write-Host $line; $err = $true }
    elseif ($line -match 'Successfully') { Write-Host $line }
  }
  if ($err) { throw "Docker build failed for $tag" }
}

if (-not $SkipImages) {
  Invoke-DockerPull "mcr.microsoft.com/dotnet/sdk:8.0"
  Invoke-DockerPull "mcr.microsoft.com/dotnet/aspnet:8.0"
  Invoke-DockerPull "nginx:1.27-alpine"

  $apiStaging = Join-Path $env:TEMP "horizon-api-ctx"
  $apiTar = Join-Path $env:TEMP "horizon-api-build.tar"
  if (Test-Path $apiStaging) { Remove-Item $apiStaging -Recurse -Force }
  New-Item -ItemType Directory -Path "$apiStaging\src\HorizonControl.Shared","$apiStaging\src\HorizonControl.Api" -Force | Out-Null
  Copy-Item (Join-Path $cfg.Root "deploy\Dockerfile.api") (Join-Path $apiStaging "Dockerfile") -Force
  if (Test-Path (Join-Path $cfg.Root "Directory.Build.props")) {
    Copy-Item (Join-Path $cfg.Root "Directory.Build.props") $apiStaging -Force
  } else { Set-Content (Join-Path $apiStaging "Directory.Build.props") "<Project />" }
  if (Test-Path (Join-Path $cfg.Root "Directory.Packages.props")) {
    Copy-Item (Join-Path $cfg.Root "Directory.Packages.props") $apiStaging -Force
  } else {
    Set-Content (Join-Path $apiStaging "Directory.Packages.props") "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>"
  }
  Copy-Item (Join-Path $cfg.Root "src\HorizonControl.Shared\*") "$apiStaging\src\HorizonControl.Shared\" -Recurse -Force
  Copy-Item (Join-Path $cfg.Root "src\HorizonControl.Api\*") "$apiStaging\src\HorizonControl.Api\" -Recurse -Force
  Push-Location $apiStaging
  if (Test-Path $apiTar) { Remove-Item $apiTar -Force }
  tar -cf $apiTar --exclude=bin --exclude=obj *
  Pop-Location
  Invoke-DockerBuild $apiTag $apiTar
  # retag versioned (best-effort via rebuild same tar)
  Invoke-DockerBuild $apiVersionTag $apiTar

  $webStaging = Join-Path $env:TEMP "horizon-web-ctx"
  $webTar = Join-Path $env:TEMP "horizon-web-build.tar"
  if (Test-Path $webStaging) { Remove-Item $webStaging -Recurse -Force }
  New-Item -ItemType Directory -Path "$webStaging\dist" -Force | Out-Null
  Copy-Item (Join-Path $cfg.Root "deploy\Dockerfile.web.prebuilt") (Join-Path $webStaging "Dockerfile") -Force
  Copy-Item (Join-Path $cfg.Root "deploy\nginx-web.conf") (Join-Path $webStaging "nginx-web.conf") -Force
  Copy-Item (Join-Path $cfg.Root "src\HorizonControl.Web\dist\*") "$webStaging\dist\" -Recurse -Force
  Push-Location $webStaging
  if (Test-Path $webTar) { Remove-Item $webTar -Force }
  tar -cf $webTar *
  Pop-Location
  Invoke-DockerBuild $webTag $webTar
  Invoke-DockerBuild $webVersionTag $webTar
}

if (-not $SkipStack) {
  $ph = @{ "X-API-Key" = $cfg.PortainerToken }
  $containers = Invoke-RestMethod -Uri "$docker/containers/json?all=true" -Headers $ph
  foreach ($c in @($containers | Where-Object { $_.Names[0] -match "horizon-fleet" })) {
    Write-Host "Removing $($c.Names[0])"
    try { Invoke-RestMethod -Uri ($docker + "/containers/" + $c.Id + "/stop?t=8") -Method Post -Headers $ph -TimeoutSec 30 | Out-Null } catch {}
    Invoke-RestMethod -Uri ($docker + "/containers/" + $c.Id + "?force=true") -Method Delete -Headers $ph -TimeoutSec 30 | Out-Null
  }
  $stacks = @(Invoke-RestMethod -Uri "$($cfg.PortainerUrl)/api/stacks" -Headers $ph)
  foreach ($s in $stacks | Where-Object { $_.Name -eq "horizon-fleet" }) {
    Write-Host "Deleting stack $($s.Id)"
    try { Invoke-RestMethod -Uri "$($cfg.PortainerUrl)/api/stacks/$($s.Id)?endpointId=$($cfg.EndpointId)" -Method Delete -Headers $ph -TimeoutSec 60 | Out-Null } catch {
      Write-Host "stack delete note: $($_.Exception.Message)"
    }
  }

  # Ensure latest compose is on GitHub for Git deploy (caller should have pushed). Create from repository.
  $payload = @{
    Name = "horizon-fleet"
    RepositoryURL = "https://github.com/lfbravini/vmware-client-controller.git"
    RepositoryReferenceName = "refs/heads/master"
    ComposeFile = "deploy/portainer-stack.yml"
    RepositoryAuthentication = $false
    TLSSkipVerify = $false
    FromAppTemplate = $false
  } | ConvertTo-Json -Compress
  $utf8 = New-Object System.Text.UTF8Encoding $false
  $bytes = $utf8.GetBytes($payload)
  $uri = "$($cfg.PortainerUrl)/api/stacks/create/standalone/repository?endpointId=$($cfg.EndpointId)"
  $req = [System.Net.HttpWebRequest]::Create($uri)
  $req.Method = "POST"; $req.ContentType = "application/json"
  $req.Headers.Add("X-API-Key", $cfg.PortainerToken)
  $req.Timeout = 300000; $req.ReadWriteTimeout = 300000
  $stream = $req.GetRequestStream(); $stream.Write($bytes, 0, $bytes.Length); $stream.Close()
  try {
    $resp = $req.GetResponse()
    $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
    Write-Host "Stack create: $($reader.ReadToEnd())"
  } catch [System.Net.WebException] {
    $er = $_.Exception.Response
    if ($er) {
      $reader = New-Object System.IO.StreamReader($er.GetResponseStream())
      throw "Stack create failed: $($reader.ReadToEnd())"
    }
    throw
  }
}

Wait-LabReady -Config $cfg -TimeoutSec 180
Write-Host "lab-redeploy OK"
