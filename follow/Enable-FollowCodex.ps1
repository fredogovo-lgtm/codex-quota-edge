[CmdletBinding()]
param([switch]$NoLaunch)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'FollowTask.psm1') -Force -DisableNameChecking
$target=Join-Path $env:LOCALAPPDATA 'Programs\CodexUsageWidget-Follow'
$stateDir=Get-FollowStateDirectory
$sourceExe=Join-Path $PSScriptRoot 'CodexUsageWidget.exe'
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) { throw 'Keep this script beside CodexUsageWidget.exe.' }
if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'CodexUsageWidgetHost.exe') -PathType Leaf)) { throw 'Keep this script beside CodexUsageWidgetHost.exe.' }
$shell=New-Object -ComObject WScript.Shell
$startup=[Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
$shortcutPath=Join-Path $startup 'Codex Usage Widget - Follow Codex.lnk'
$launcher=Join-Path $target 'Start-Follow.vbs'
if (Test-Path -LiteralPath $shortcutPath) {
    $old=$shell.CreateShortcut($shortcutPath)
    if ($old.Arguments -ne ('"'+$launcher+'"')) { throw 'An unrelated startup shortcut uses this name; leaving it untouched.' }
}
# Check task ownership before making any installation changes.
$null=Get-OwnedFollowTask -InstallDirectory $target -StateDirectory $stateDir
# Ask the existing owned helper to stop before replacing its files.
& (Join-Path $PSScriptRoot 'Disable-FollowCodex.ps1') -KeepStartupShortcut
New-Item -ItemType Directory -Path $target -Force | Out-Null
foreach ($name in @('CodexUsageWidget.exe','CodexUsageWidgetHost.exe','CodexLifecycle.psm1','FollowCodex.ps1','FollowTask.psm1','Launch-FollowCodex.ps1','Start-Follow.vbs','Enable-Follow.vbs','Disable-Follow.vbs','Enable-FollowCodex.ps1','Disable-FollowCodex.ps1','Start-Follow.cmd','Enable-Follow.cmd','Disable-Follow.cmd','README-follow.md','README-upstream.md','LICENSE','SECURITY.md','THIRD_PARTY_NOTICES.md','UPSTREAM.json')) {
    $source=Join-Path $PSScriptRoot $name
    if ((Test-Path -LiteralPath $source -PathType Leaf) -and -not [string]::Equals([IO.Path]::GetFullPath($source),[IO.Path]::GetFullPath((Join-Path $target $name)),[StringComparison]::OrdinalIgnoreCase)) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $target $name) -Force
    }
}
$task=Register-FollowTask -InstallDirectory $target -StateDirectory $stateDir
# Migrate the owned legacy login shortcut. Task Scheduler now owns both login
# startup and process lifetime; keeping two startup entries is unnecessary.
if(Test-Path -LiteralPath $shortcutPath){Remove-Item -LiteralPath $shortcutPath -Force}
foreach($marker in @('disabled','stop.request')) {
    $markerPath=Join-Path $stateDir $marker
    if(Test-Path -LiteralPath $markerPath){Remove-Item -LiteralPath $markerPath -Force}
}
if (-not $NoLaunch) {
    Start-FollowTask -InstallDirectory $target -StateDirectory $stateDir
}
Write-Output ('Codex follow enabled: '+$target)
Write-Output ('Independent current-user task: '+$task.Name)
