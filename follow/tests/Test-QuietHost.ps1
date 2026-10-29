[CmdletBinding()]
param([string]$HostPath='')
$ErrorActionPreference='Stop'
if(-not$HostPath){$HostPath=Join-Path (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))) 'work\follow-host-v2.2\CodexUsageWidgetHost.exe'}
if(-not(Test-Path -LiteralPath $HostPath -PathType Leaf)){throw 'Quiet host has not been built.'}
$root=Join-Path $env:TEMP ('CodexQuietHostTest-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$hostCopy=Join-Path $root 'CodexUsageWidgetHost.exe'
Copy-Item -LiteralPath $HostPath -Destination $hostCopy
$utf8=New-Object Text.UTF8Encoding($true)
$probe=@'
param([string]$StateDirectory)
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class QuietConsoleProbe { [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow(); }'
$root=if($StateDirectory){$StateDirectory}else{$PSScriptRoot}
[IO.File]::WriteAllText((Join-Path $root 'result.json'),(@{Pid=$PID;Console=[QuietConsoleProbe]::GetConsoleWindow().ToInt64();Mode=[IO.Path]::GetFileName($MyInvocation.MyCommand.Path)}|ConvertTo-Json))
Start-Sleep -Milliseconds 900
'@
foreach($script in @('FollowCodex.ps1','Launch-FollowCodex.ps1','Enable-FollowCodex.ps1','Disable-FollowCodex.ps1')){[IO.File]::WriteAllText((Join-Path $root $script),$probe,$utf8)}
$count=0
function Check([string]$Label,[bool]$Value){if(-not$Value){throw ('FAIL: '+$Label)};$script:count++;Write-Output ('PASS: '+$Label)}
$bytes=[IO.File]::ReadAllBytes($hostCopy)
$peOffset=[BitConverter]::ToInt32($bytes,0x3c)
Check 'Launcher uses Windows GUI subsystem rather than Console subsystem' ([BitConverter]::ToUInt16($bytes,$peOffset+24+68) -eq 2)
foreach($mode in @('monitor','start','enable','disable')) {
 $result=Join-Path $root 'result.json'
 if(Test-Path -LiteralPath $result){Remove-Item -LiteralPath $result -Force}
 $arguments=if($mode -eq 'monitor'){'--monitor --state-directory "'+$root+'"'}else{'--'+$mode}
 $p=Start-Process -FilePath $hostCopy -ArgumentList $arguments -PassThru
 $limit=[datetime]::UtcNow.AddSeconds(15)
 while(-not(Test-Path -LiteralPath $result) -and [datetime]::UtcNow -lt $limit){Start-Sleep -Milliseconds 50}
 Check ($mode+' launches its fixed script') (Test-Path -LiteralPath $result)
 $sample=Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
 Check ($mode+' PowerShell child has no console window') ($sample.Console -eq 0)
 Check ($mode+' GUI host stays alive while its child runs') (-not$p.HasExited)
 if(-not$p.WaitForExit(10000)){throw 'Quiet host did not finish'}
 Check ($mode+' propagates a successful exit') ($p.ExitCode -eq 0)
}
[IO.File]::WriteAllText((Join-Path $root 'FollowCodex.ps1'),'param([string]$StateDirectory) exit 23',$utf8)
$p=Start-Process -FilePath $hostCopy -ArgumentList ('--monitor --state-directory "'+$root+'"') -PassThru
if(-not$p.WaitForExit(10000)){throw 'Quiet host did not finish failure probe'}
Check 'Background monitor propagates failure so scheduled recovery can work' ($p.ExitCode -eq 23)
Write-Output ("$count quiet-launch checks passed.")
