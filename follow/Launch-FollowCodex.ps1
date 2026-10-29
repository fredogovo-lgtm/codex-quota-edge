[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'FollowTask.psm1') -Force -DisableNameChecking
$state=Get-FollowStateDirectory
$installed=Join-Path $env:LOCALAPPDATA 'Programs\CodexUsageWidget-Follow'
Start-FollowTask -InstallDirectory $installed -StateDirectory $state
