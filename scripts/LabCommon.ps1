# Shared helpers for lab-*.ps1
$script:LabRoot = Split-Path $PSScriptRoot -Parent
$script:LabEdgeDefault = "http://172.16.100.81:18080"
$script:LabAdminKeyDefault = "lab-admin-key-change-me"
$script:LabEnrollDefault = "lab-enroll-token-change-me"
$script:LabLogDir = Join-Path $LabRoot "deploy\lab-run-logs"

function Get-LabConfig {
  param(
    [string] $EdgeUrl = $env:HORIZON_SERVER,
    [string] $AdminKey = $env:HORIZON_ADMIN_KEY,
    [string] $EnrollToken = $env:HORIZON_ENROLLMENT_TOKEN
  )
  if ([string]::IsNullOrWhiteSpace($EdgeUrl)) { $EdgeUrl = $LabEdgeDefault }
  if ([string]::IsNullOrWhiteSpace($AdminKey)) { $AdminKey = $LabAdminKeyDefault }
  if ([string]::IsNullOrWhiteSpace($EnrollToken)) { $EnrollToken = $LabEnrollDefault }
  $tokenPath = Join-Path $LabRoot "docs\api.txt"
  $portainerToken = $null
  if (Test-Path $tokenPath) {
    $raw = (Get-Content $tokenPath -Raw).Trim()
    $portainerToken = ($raw -replace '^portainer\s+', '').Trim()
  }
  [pscustomobject]@{
    Root = $LabRoot
    EdgeUrl = $EdgeUrl.TrimEnd('/')
    AdminKey = $AdminKey
    EnrollToken = $EnrollToken
    PortainerUrl = "https://172.16.100.81:9443"
    PortainerToken = $portainerToken
    EndpointId = 3
    LogDir = $LabLogDir
  }
}

function Enable-LabTlsBypass {
  add-type @"
using System.Net;using System.Security.Cryptography.X509Certificates;
public class LabCert : ICertificatePolicy { public bool CheckValidationResult(ServicePoint s, X509Certificate c, WebRequest r, int p){return true;} }
"@ -ErrorAction SilentlyContinue
  [System.Net.ServicePointManager]::CertificatePolicy = New-Object LabCert
  [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
}

function Get-PortainerHeaders {
  param([Parameter(Mandatory)] $Config)
  if ([string]::IsNullOrWhiteSpace($Config.PortainerToken)) {
    throw "Missing Portainer API token. Put 'portainer ptr_...' in docs/api.txt (gitignored)."
  }
  @{ "X-API-Key" = $Config.PortainerToken }
}

function Invoke-LabAdmin {
  param(
    [Parameter(Mandatory)] $Config,
    [Parameter(Mandatory)] [string] $Path,
    [string] $Method = "GET",
    $Body = $null
  )
  $uri = "$($Config.EdgeUrl)$Path"
  $headers = @{ "X-Admin-Api-Key" = $Config.AdminKey; Accept = "application/json" }
  if ($null -ne $Body) {
    $headers["Content-Type"] = "application/json"
    $json = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Compress -Depth 8 }
    return Invoke-RestMethod -Uri $uri -Method $Method -Headers $headers -Body $json -TimeoutSec 30
  }
  return Invoke-RestMethod -Uri $uri -Method $Method -Headers $headers -TimeoutSec 30
}

function Wait-LabReady {
  param([Parameter(Mandatory)] $Config, [int] $TimeoutSec = 120)
  $deadline = (Get-Date).AddSeconds($TimeoutSec)
  while ((Get-Date) -lt $deadline) {
    try {
      $h = Invoke-WebRequest -Uri "$($Config.EdgeUrl)/health" -UseBasicParsing -TimeoutSec 5
      $ui = Invoke-WebRequest -Uri "$($Config.EdgeUrl)/" -UseBasicParsing -TimeoutSec 5
      $t = Invoke-LabAdmin -Config $Config -Path "/api/v1/admin/tenants"
      if ($h.StatusCode -eq 200 -and $ui.Content -match "Horizon Fleet" -and @($t).Count -ge 1) {
        return $true
      }
    } catch { }
    Start-Sleep 3
  }
  throw "Edge not ready at $($Config.EdgeUrl) within ${TimeoutSec}s"
}

function Ensure-LabLogDir {
  param($Config)
  New-Item -ItemType Directory -Force -Path $Config.LogDir | Out-Null
}
