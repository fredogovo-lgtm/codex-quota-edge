$ErrorActionPreference='Stop'

function Get-FollowStateDirectory {
    # Packaged Codex children can see a virtualized AppData tree while Task
    # Scheduler sees the physical tree. Profile data has one shared view.
    return (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) '.codex-usage-widget\follow')
}
function Get-FollowTaskName {
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    return ('Codex Usage Widget - Follow Codex - '+$sid)
}
function Get-FollowTaskArguments([string]$InstallDirectory,[string]$StateDirectory) {
    return ('--monitor --state-directory "'+[IO.Path]::GetFullPath($StateDirectory).TrimEnd('\')+'"')
}
function Get-FollowTaskDescription([string]$InstallDirectory) {
    return ('CodexUsageWidget-Follow-v2.2; user='+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value+'; install='+[IO.Path]::GetFullPath($InstallDirectory))
}
function Get-FollowScheduler {
    $service=New-Object -ComObject Schedule.Service
    $service.Connect()
    return $service
}
function Get-OwnedFollowTask {
    param([string]$InstallDirectory,[string]$StateDirectory,[string]$TaskName=(Get-FollowTaskName))
    $service=Get-FollowScheduler
    try { $task=$service.GetFolder('\').GetTask($TaskName) }
    catch {
        if($_.Exception.HResult -eq -2147024894){return $null}
        throw
    }
    $d=$task.Definition
    $expectedPowerShell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $principalSid=$d.Principal.UserId
    if($principalSid -notmatch '^S-1-') {
        try {$principalSid=(New-Object Security.Principal.NTAccount($principalSid)).Translate([Security.Principal.SecurityIdentifier]).Value} catch {throw 'Task principal cannot be verified.'}
    }
    $expectedSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $validAction=$false
    if($d.Actions.Count -eq 1 -and $d.Actions.Item(1).Type -eq 0) {
        $a=$d.Actions.Item(1)
        $current=($d.RegistrationInfo.Description -ceq (Get-FollowTaskDescription $InstallDirectory) -and
            [string]::Equals($a.Path,(Join-Path ([IO.Path]::GetFullPath($InstallDirectory)) 'CodexUsageWidgetHost.exe'),[StringComparison]::OrdinalIgnoreCase) -and
            $a.Arguments -ceq (Get-FollowTaskArguments $InstallDirectory $StateDirectory))
        # Accept only the exact previous owned action during an in-place upgrade.
        $legacyDescription='CodexUsageWidget-Follow-v2.1; user='+$expectedSid+'; install='+[IO.Path]::GetFullPath($InstallDirectory)
        $legacyArguments='-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+(Join-Path ([IO.Path]::GetFullPath($InstallDirectory)) 'FollowCodex.ps1')+'" -StateDirectory "'+[IO.Path]::GetFullPath($StateDirectory)+'"'
        $legacy=($d.RegistrationInfo.Description -ceq $legacyDescription -and
            [string]::Equals($a.Path,$expectedPowerShell,[StringComparison]::OrdinalIgnoreCase) -and $a.Arguments -ceq $legacyArguments)
        $validAction=($current -or $legacy) -and [string]::Equals($a.WorkingDirectory,[IO.Path]::GetFullPath($InstallDirectory),[StringComparison]::OrdinalIgnoreCase)
    }
    if(-not $validAction -or
        $d.Principal.LogonType -ne 3 -or $d.Principal.RunLevel -ne 0 -or $principalSid -cne $expectedSid) {
        throw 'A task with this name does not belong to this installation; it was left untouched.'
    }
    return $task
}
function Register-FollowTask {
    param([string]$InstallDirectory,[string]$StateDirectory,[string]$TaskName=(Get-FollowTaskName))
    $null=Get-OwnedFollowTask -InstallDirectory $InstallDirectory -StateDirectory $StateDirectory -TaskName $TaskName
    if(-not(Test-Path -LiteralPath (Join-Path $InstallDirectory 'CodexUsageWidgetHost.exe') -PathType Leaf)){throw 'The quiet background host is missing. Extract the complete package.'}
    $service=Get-FollowScheduler
    $def=$service.NewTask(0)
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $def.RegistrationInfo.Description=Get-FollowTaskDescription $InstallDirectory
    $def.Principal.UserId=$sid
    $def.Principal.LogonType=3 # current interactive user, no stored password
    $def.Principal.RunLevel=0
    $def.Settings.Enabled=$true
    $def.Settings.Hidden=$true
    $def.Settings.MultipleInstances=2 # IgnoreNew; the helper also has a per-state mutex
    $def.Settings.ExecutionTimeLimit='PT0S'
    $def.Settings.AllowDemandStart=$true
    $def.Settings.DisallowStartIfOnBatteries=$false
    $def.Settings.StopIfGoingOnBatteries=$false
    $def.Settings.StartWhenAvailable=$true
    $def.Settings.RestartInterval='PT1M'
    $def.Settings.RestartCount=3
    $logon=$def.Triggers.Create(9)
    $logon.UserId=$sid
    $logon.Enabled=$true
    # A periodic trigger also recovers a helper terminated without a failure code.
    # IgnoreNew prevents a second helper while the monitoring task is healthy.
    $watchdog=$def.Triggers.Create(1)
    $watchdog.StartBoundary=[datetime]::Now.AddMinutes(1).ToString('yyyy-MM-ddTHH:mm:ss')
    $watchdog.Repetition.Interval='PT1M'
    $watchdog.Repetition.StopAtDurationEnd=$false
    $watchdog.Enabled=$true
    $action=$def.Actions.Create(0)
    $action.Path=Join-Path ([IO.Path]::GetFullPath($InstallDirectory)) 'CodexUsageWidgetHost.exe'
    $action.Arguments=Get-FollowTaskArguments $InstallDirectory $StateDirectory
    $action.WorkingDirectory=[IO.Path]::GetFullPath($InstallDirectory)
    $null=$service.GetFolder('\').RegisterTaskDefinition($TaskName,$def,6,$sid,$null,3,$null)
    return Get-OwnedFollowTask -InstallDirectory $InstallDirectory -StateDirectory $StateDirectory -TaskName $TaskName
}
function Start-FollowTask {
    param([string]$InstallDirectory,[string]$StateDirectory,[string]$TaskName=(Get-FollowTaskName))
    if(Test-Path -LiteralPath (Join-Path $StateDirectory 'disabled')) {return}
    $task=Get-OwnedFollowTask -InstallDirectory $InstallDirectory -StateDirectory $StateDirectory -TaskName $TaskName
    if(-not $task){throw 'Follow task is missing. Run Enable-Follow.vbs to repair the installation.'}
    if(-not $task.Enabled){throw 'Follow task is disabled. Run Enable-Follow.vbs to enable it.'}
    $null=$task.Run($null)
}
function Disable-FollowTask {
    param([string]$InstallDirectory,[string]$StateDirectory,[string]$TaskName=(Get-FollowTaskName))
    $task=Get-OwnedFollowTask -InstallDirectory $InstallDirectory -StateDirectory $StateDirectory -TaskName $TaskName
    if($task){$task.Enabled=$false}
}
function Remove-FollowTask {
    param([string]$InstallDirectory,[string]$StateDirectory,[string]$TaskName=(Get-FollowTaskName))
    $task=Get-OwnedFollowTask -InstallDirectory $InstallDirectory -StateDirectory $StateDirectory -TaskName $TaskName
    if($task){
        $task.Enabled=$false
        $service=Get-FollowScheduler
        $service.GetFolder('\').DeleteTask($TaskName,0)
    }
}
Export-ModuleMember -Function Get-FollowStateDirectory,Get-FollowTaskName,Get-FollowTaskArguments,Get-OwnedFollowTask,Register-FollowTask,Start-FollowTask,Disable-FollowTask,Remove-FollowTask
