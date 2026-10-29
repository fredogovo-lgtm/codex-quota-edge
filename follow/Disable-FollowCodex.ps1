[CmdletBinding()]
param([switch]$KeepStartupShortcut)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'FollowTask.psm1') -Force -DisableNameChecking
$stateDir=Get-FollowStateDirectory
$target=Join-Path $env:LOCALAPPDATA 'Programs\CodexUsageWidget-Follow'
# Persist intent before inspecting status: the helper may still be starting and
# not yet have written its first status file.
New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $stateDir 'disabled'),'disabled')
[IO.File]::WriteAllText((Join-Path $stateDir 'stop.request'),'stop')
# Disable triggers before waiting. A pending launch also observes disabled.
Disable-FollowTask -InstallDirectory $target -StateDirectory $stateDir
$shortcutPath=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)) 'Codex Usage Widget - Follow Codex.lnk'
if (-not $KeepStartupShortcut -and (Test-Path -LiteralPath $shortcutPath)) {
    $shell=New-Object -ComObject WScript.Shell
    $link=$shell.CreateShortcut($shortcutPath)
    if ($link.Arguments -ne ('"'+(Join-Path $target 'Start-Follow.vbs')+'"')) { throw 'Startup shortcut does not belong to this tool.' }
    Remove-Item -LiteralPath $shortcutPath -Force
}
$statusPath=Join-Path $stateDir 'follow-status.json'
$status=$null
if (Test-Path -LiteralPath $statusPath) {
    try { $status=Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json } catch { }
}
if ($status -and $status.Mode -eq 'Monitoring') {
    $helper=Get-Process -Id $status.HelperProcessId -ErrorAction SilentlyContinue
    if ($helper -and $helper.StartTime.ToUniversalTime().Ticks -eq ([datetime]::Parse($status.HelperStartedUtc).ToUniversalTime().Ticks)) {
        # The v2.2 GUI parent holds the installed host executable while it waits
        # for this helper. Wait for that exact parent before an upgrade copies it.
        $monitorHost=$null
        try {
            $record=Get-CimInstance Win32_Process -Filter ('ProcessId='+$helper.Id) -ErrorAction Stop
            $parent=Get-Process -Id $record.ParentProcessId -ErrorAction Stop
            if([string]::Equals($parent.Path,(Join-Path $target 'CodexUsageWidgetHost.exe'),[StringComparison]::OrdinalIgnoreCase)){$monitorHost=$parent}
        } catch { }
        if (-not $helper.WaitForExit(15000)) { throw 'Helper did not stop. No process was forcibly terminated.' }
        if($monitorHost -and -not$monitorHost.WaitForExit(5000)){throw 'Background host did not finish. No process was forcibly terminated.'}
    }
}
Remove-FollowTask -InstallDirectory $target -StateDirectory $stateDir
Write-Output 'Codex follow disabled. Files and appearance settings are preserved.'
