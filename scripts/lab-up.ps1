#Requires -Version 5.1
<#
.SYNOPSIS
  Full lab loop: redeploy → agents → test (UC1–UC5).
#>
[CmdletBinding()]
param(
  [string] $EdgeUrl,
  [switch] $SkipRedeploy,
  [switch] $SkipAgents,
  [switch] $SkipHang
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot

Write-Host "======== lab-up START ========"
if (-not $SkipRedeploy) {
  & (Join-Path $here "lab-redeploy.ps1") -EdgeUrl $EdgeUrl
}
if (-not $SkipAgents) {
  & (Join-Path $here "lab-agents.ps1") -EdgeUrl $EdgeUrl
}
$testArgs = @{}
if ($EdgeUrl) { $testArgs.EdgeUrl = $EdgeUrl }
if ($SkipHang) { $testArgs.SkipHang = $true }
& (Join-Path $here "lab-test.ps1") @testArgs
Write-Host "======== lab-up DONE ========"
