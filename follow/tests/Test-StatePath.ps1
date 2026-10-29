$ErrorActionPreference='Stop'
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'FollowTask.psm1') -Force -DisableNameChecking
if(-not(Get-Command Get-FollowStateDirectory -ErrorAction SilentlyContinue)){throw 'FAIL: canonical shared state directory has not been implemented'}
$expected=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) '.codex-usage-widget\follow'
$actual=Get-FollowStateDirectory
if($actual -cne $expected){throw 'FAIL: follow default state is not the shared Profile directory'}
$install=Join-Path $env:LOCALAPPDATA 'Programs\CodexUsageWidget-Follow'
$arguments=Get-FollowTaskArguments -InstallDirectory $install -StateDirectory $actual
if(-not$arguments.EndsWith('--state-directory "'+$expected+'"',[StringComparison]::Ordinal)){throw 'FAIL: scheduled action must explicitly pass the shared state directory'}
Write-Output 'PASS: default shared state and scheduled action agree outside MSIX AppData redirection'
