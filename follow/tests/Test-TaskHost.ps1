[CmdletBinding()]
param([string]$ModulePath='',[string]$HostPath='',[string]$TestDirectory=(Join-Path $env:TEMP ('CodexFollowTaskTest-'+[guid]::NewGuid().ToString('N'))))
$ErrorActionPreference='Stop'
if(-not$HostPath){$HostPath=Join-Path (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))) 'work\follow-host-v2.2\CodexUsageWidgetHost.exe'}
if(-not $ModulePath){$ModulePath=Join-Path (Split-Path -Parent $PSScriptRoot) 'FollowTask.psm1'}
Import-Module $ModulePath -Force -DisableNameChecking
New-Item -ItemType Directory -Path $TestDirectory -Force | Out-Null
$runId=[guid]::NewGuid().ToString('N')
$install=Join-Path $TestDirectory $runId
$state=Join-Path $install 'state'
New-Item -ItemType Directory -Path $state -Force | Out-Null
$name='CodexUsageWidget-Test-'+$runId
$pidFile=Join-Path $state 'helper.pid'
$task=$null; $helper=$null; $hostProcess=$null
$job=[IntPtr]::Zero
$passed=0
function Check([string]$Label,[bool]$Value){if(-not$Value){throw ('FAIL: '+$Label)}; $script:passed++; Write-Output ('PASS: '+$Label)}
function Wait-Pid([int]$OldId=0,[int]$Seconds=12){
 $until=[datetime]::UtcNow.AddSeconds($Seconds)
 do {
  if(Test-Path -LiteralPath $pidFile){$id=[int](Get-Content -LiteralPath $pidFile -Raw); if($id -ne $OldId){$p=Get-Process -Id $id -ErrorAction SilentlyContinue; if($p){return $p}}}
  Start-Sleep -Milliseconds 100
 } while([datetime]::UtcNow -lt $until)
 throw 'Scheduled helper did not appear within expected recovery interval'
}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FollowHostJob {
 [StructLayout(LayoutKind.Sequential)] public struct Basic { public long PerProcess,PerJob; public uint Flags; public UIntPtr Min,Max; public uint Active; public UIntPtr Affinity; public uint Priority,Scheduling; }
 [StructLayout(LayoutKind.Sequential)] public struct Io { public ulong Read,Write,Other,ReadBytes,WriteBytes,OtherBytes; }
 [StructLayout(LayoutKind.Sequential)] public struct Extended { public Basic Basic; public Io Io; public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob; }
 [DllImport("kernel32.dll",SetLastError=true)] public static extern IntPtr CreateJobObject(IntPtr security,string name);
 [DllImport("kernel32.dll",SetLastError=true)] public static extern bool SetInformationJobObject(IntPtr job,int type,ref Extended info,uint size);
 [DllImport("kernel32.dll",SetLastError=true)] public static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
 [DllImport("kernel32.dll",SetLastError=true)] public static extern bool IsProcessInJob(IntPtr process,IntPtr job,out bool result);
 [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
}
'@
try {
 Copy-Item -LiteralPath $HostPath -Destination (Join-Path $install 'CodexUsageWidgetHost.exe')
 [IO.File]::WriteAllText((Join-Path $install 'FollowCodex.ps1'), @'
param([string]$StateDirectory)
if(Test-Path -LiteralPath (Join-Path $StateDirectory 'disabled')){exit 0}
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class ScheduledConsoleProbe { [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow(); }'
[IO.File]::WriteAllText((Join-Path $StateDirectory 'console.handle'),[string][ScheduledConsoleProbe]::GetConsoleWindow().ToInt64())
[IO.File]::WriteAllText((Join-Path $StateDirectory 'helper.pid'),[string]$PID)
while(-not(Test-Path -LiteralPath (Join-Path $StateDirectory 'disabled')) -and -not(Test-Path -LiteralPath (Join-Path $StateDirectory 'stop.request'))){Start-Sleep -Milliseconds 100}
'@,(New-Object Text.UTF8Encoding($true)))
 $service=New-Object -ComObject Schedule.Service; $service.Connect()
 $definition=$service.NewTask(0); $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
 $definition.Principal.UserId=$sid; $definition.Principal.LogonType=3
 $definition.RegistrationInfo.Description='CodexUsageWidget-Follow-v2.1; user='+$sid+'; install='+$install
 $a=$definition.Actions.Create(0); $a.Path=$env:WINDIR+'\System32\WindowsPowerShell\v1.0\powershell.exe'
 $a.Arguments='-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+(Join-Path $install 'FollowCodex.ps1')+'" -StateDirectory "'+$state+'"'
 $a.WorkingDirectory=$install
 $null=$service.GetFolder('\').RegisterTaskDefinition($name,$definition,6,$sid,$null,3,$null)
 Check 'Exact owned v2.1 task can be upgraded without touching another task' ([bool](Get-OwnedFollowTask -InstallDirectory $install -StateDirectory $state -TaskName $name))
 $task=Register-FollowTask -InstallDirectory $install -StateDirectory $state -TaskName $name
 $d=$task.Definition
 Check 'Task directly starts the GUI host with exact background arguments' ($d.Actions.Item(1).Path -ceq (Join-Path $install 'CodexUsageWidgetHost.exe') -and $d.Actions.Item(1).Arguments -ceq ('--monitor --state-directory "'+$state+'"'))
 Check 'Task runs as interactive current user without elevation or password' ($d.Principal.LogonType -eq 3 -and $d.Principal.RunLevel -eq 0)
 Check 'Task has no time limit and remains enabled on battery' ($d.Settings.ExecutionTimeLimit -eq 'PT0S' -and -not$d.Settings.DisallowStartIfOnBatteries -and -not$d.Settings.StopIfGoingOnBatteries)
 Check 'Task has logon and recurring recovery triggers' ($d.Triggers.Count -eq 2 -and $d.Triggers.Item(1).Type -eq 9 -and $d.Triggers.Item(2).Repetition.Interval -eq 'PT1M')
 Check 'Task ignores concurrent launches and does not wake the PC' ($d.Settings.MultipleInstances -eq 2 -and -not$d.Settings.WakeToRun)
 # Launch from a short-lived process, then verify the monitor survives its exit.
 $launcher=Join-Path $install 'caller.ps1'
 $code='Start-Sleep -Seconds 2; Import-Module '''+$ModulePath.Replace("'","''")+''' -Force -DisableNameChecking; Start-FollowTask -InstallDirectory '''+$install.Replace("'","''")+''' -StateDirectory '''+$state.Replace("'","''")+''' -TaskName '''+$name+''''
 [IO.File]::WriteAllText($launcher,$code,(New-Object Text.UTF8Encoding($true)))
 $start=New-Object Diagnostics.ProcessStartInfo
 $start.FileName=$env:WINDIR+'\System32\WindowsPowerShell\v1.0\powershell.exe'
 $start.Arguments='-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+$launcher+'"'
 $start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
 $hostProcess=[Diagnostics.Process]::Start($start)
 $job=[FollowHostJob]::CreateJobObject([IntPtr]::Zero,$null)
 $jobInfo=New-Object FollowHostJob+Extended; $jobInfo.Basic.Flags=0x2000
 if(-not [FollowHostJob]::SetInformationJobObject($job,9,[ref]$jobInfo,[Runtime.InteropServices.Marshal]::SizeOf($jobInfo)) -or -not [FollowHostJob]::AssignProcessToJobObject($job,$hostProcess.Handle)){throw 'Cannot create controlled launcher job'}
 $helper=Wait-Pid
 Check 'Scheduled monitor PowerShell has no console window' ([long](Get-Content -LiteralPath (Join-Path $state 'console.handle') -Raw) -eq 0)
 $inHostJob=$false
 if(-not [FollowHostJob]::IsProcessInJob($helper.Handle,$job,[ref]$inHostJob)){throw 'Cannot verify scheduled helper job ownership'}
 Check 'Scheduled helper does not inherit the launcher host job' (-not$inHostJob)
 $null=$hostProcess.WaitForExit(10000)
 Start-Sleep -Milliseconds 400
 $helper.Refresh()
 Check 'Scheduled helper remains alive after its launcher has exited' ($hostProcess.HasExited -and -not$helper.HasExited)
 $before=$helper.Id
 Start-FollowTask -InstallDirectory $install -StateDirectory $state -TaskName $name
 Start-Sleep -Milliseconds 700
 Check 'A second launch retains the same single helper' ([int](Get-Content -LiteralPath $pidFile -Raw) -eq $before)
 $helper.Kill(); $null=$helper.WaitForExit(5000)
 $helper=Wait-Pid -OldId $before -Seconds 75
 Check 'Minute trigger recovers an unexpectedly terminated helper' ($helper.Id -ne $before)
 [IO.File]::WriteAllText((Join-Path $state 'disabled'),'disabled')
 [IO.File]::WriteAllText((Join-Path $state 'stop.request'),'stop')
 Disable-FollowTask -InstallDirectory $install -StateDirectory $state -TaskName $name
 Check 'Disable prevents recovery before waiting for helper exit' (-not (Get-OwnedFollowTask -InstallDirectory $install -StateDirectory $state -TaskName $name).Enabled)
 $null=$helper.WaitForExit(5000)
 Remove-FollowTask -InstallDirectory $install -StateDirectory $state -TaskName $name
 Check 'Stop and removal cleanly delete the owned task and helper' ($helper.HasExited -and -not(Get-OwnedFollowTask -InstallDirectory $install -StateDirectory $state -TaskName $name))
 $task=Register-FollowTask -InstallDirectory $install -StateDirectory $state -TaskName $name
 Start-FollowTask -InstallDirectory $install -StateDirectory $state -TaskName $name
 Start-Sleep -Milliseconds 500
 Check 'Persisted disable blocks a pending demand launch' ($task.State -ne 4)
 Remove-FollowTask -InstallDirectory $install -StateDirectory $state -TaskName $name
 $service=New-Object -ComObject Schedule.Service; $service.Connect()
 $definition=$service.NewTask(0); $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
 $definition.Principal.UserId=$sid; $definition.Principal.LogonType=3; $a=$definition.Actions.Create(0); $a.Path=$env:WINDIR+'\System32\cmd.exe'; $a.Arguments='/c exit 0'
 $null=$service.GetFolder('\').RegisterTaskDefinition($name,$definition,6,$sid,$null,3,$null)
 $refused=$false
 try {Remove-FollowTask -InstallDirectory $install -StateDirectory $state -TaskName $name} catch {$refused=$true}
 Check 'A same-name unrelated task is never modified or deleted' ($refused -and $service.GetFolder('\').GetTask($name).Definition.Actions.Item(1).Path -like '*cmd.exe')
 $service.GetFolder('\').DeleteTask($name,0)
 Write-Output ("$passed independent-host checks passed.")
} finally {
 if($job -ne [IntPtr]::Zero){$null=[FollowHostJob]::CloseHandle($job)}
 [IO.File]::WriteAllText((Join-Path $state 'disabled'),'disabled')
 if($helper){try{if(-not$helper.HasExited){$null=$helper.WaitForExit(3000);if(-not$helper.HasExited){$helper.Kill()}}}catch{}}
 $service=New-Object -ComObject Schedule.Service; $service.Connect()
 try{$service.GetFolder('\').DeleteTask($name,0)}catch{}
}
