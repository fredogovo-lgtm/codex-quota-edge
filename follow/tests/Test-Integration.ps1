[CmdletBinding()]
param([string]$TestDirectory = (Join-Path $env:TEMP ('CodexFollowTest-'+[guid]::NewGuid().ToString('N'))),[string]$RunnerPath='')
$ErrorActionPreference='Stop'
$TestDirectory=[IO.Path]::GetFullPath($TestDirectory)
New-Item -ItemType Directory -Path $TestDirectory -Force | Out-Null
$desktopPath=Join-Path $TestDirectory 'DesktopFixture.exe'
$widgetPath=Join-Path $TestDirectory 'WidgetFixture.exe'
$stateDir=Join-Path $TestDirectory 'state'
$runner=Join-Path (Split-Path -Parent $PSScriptRoot) 'FollowCodex.ps1'
if($RunnerPath){$runner=[IO.Path]::GetFullPath($RunnerPath)}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$desktop=$null; $helper=$null; $extra=$null; $externalWidget=$null
$passed=0
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class FixtureWindows {
    private delegate bool Callback(IntPtr window,IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback,IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window,uint msg,IntPtr a,IntPtr b);
    private static IntPtr Find(int pid,bool visibleOnly) {
        IntPtr found=IntPtr.Zero;
        EnumWindows(delegate(IntPtr h,IntPtr ignored) {
            uint owner; GetWindowThreadProcessId(h,out owner);
            if(owner!=(uint)pid || (visibleOnly && !IsWindowVisible(h))) return true;
            var text=new StringBuilder(512); GetWindowText(h,text,512);
            if(text.ToString()=="Codex \u7528\u91cf\u60ac\u6d6e\u7a97") { found=h; return false; }
            return true;
        },IntPtr.Zero);
        return found;
    }
    public static bool Visible(int pid) { return Find(pid,true)!=IntPtr.Zero; }
    public static bool Close(int pid) {
        var h=Find(pid,false);
        return h!=IntPtr.Zero && PostMessage(h,0x0010,IntPtr.Zero,IntPtr.Zero);
    }
}
'@
function Wait-For([string]$Name,[scriptblock]$Condition,[int]$Seconds=12) {
    $until=[datetime]::UtcNow.AddSeconds($Seconds)
    do { if (& $Condition) { Write-Output "PASS: $Name"; $script:passed++; return }; Start-Sleep -Milliseconds 150 } while ([datetime]::UtcNow -lt $until)
    throw "Timeout: $Name"
}
function Read-Status {
    try { Get-Content -LiteralPath (Join-Path $stateDir 'follow-status.json') -Raw | ConvertFrom-Json } catch { $null }
}
function Start-Helper {
    $args='-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+$runner+'" -WidgetPath "'+$widgetPath+'" -DesktopExecutablePath "'+$desktopPath+'" -StateDirectory "'+$stateDir+'" -PollIntervalSeconds 0.2'
    Start-Process -FilePath (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList $args -WindowStyle Hidden -PassThru
}
function New-Desktop { Start-Process -FilePath $desktopPath -WindowStyle Hidden -PassThru }
function Running([int]$ProcessId) { $null -ne (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) }
function End-Fixture($Process) { if ($null -ne $Process -and -not $Process.HasExited) { $Process.Kill(); $null=$Process.WaitForExit(3000) } }
try {
    $desktopSource=Join-Path $TestDirectory 'desktop.cs'
    $widgetSource=Join-Path $TestDirectory 'widget.cs'
    [IO.File]::WriteAllText($desktopSource, 'using System.Threading; class D { static void Main() { Thread.Sleep(300000); } }')
    [IO.File]::WriteAllText($widgetSource, @'
using System;
using System.IO;
using System.Windows.Forms;
class W {
    [STAThread] static void Main() {
        var f=new Form(); f.Text="Codex \u7528\u91cf\u60ac\u6d6e\u7a97";
        f.Width=180; f.Height=70; f.ShowInTaskbar=false;
        f.FormClosed += delegate { File.WriteAllText(Path.Combine(Application.StartupPath,"graceful-close.txt"),"closed"); };
        f.Shown += delegate {
            if(File.Exists(Path.Combine(Application.StartupPath,"hidden.fixture"))) {
                f.Hide(); var edge=new Form(); edge.Text="Edge Fixture";
                edge.Width=40; edge.Height=60; edge.ShowInTaskbar=false; edge.Show();
            }
        };
        Application.Run(f);
    }
}
'@)
    & $compiler /nologo /target:winexe "/out:$desktopPath" $desktopSource
    if ($LASTEXITCODE -ne 0) { throw 'Desktop fixture compile failed' }
    & $compiler /nologo /target:winexe /reference:System.Windows.Forms.dll "/out:$widgetPath" $widgetSource
    if ($LASTEXITCODE -ne 0) { throw 'Widget fixture compile failed' }
    $helper=Start-Helper
    Wait-For 'Idle helper runs without launching widget' { $s=Read-Status; $s -and $s.DesktopStatus -eq 'Stopped' -and -not $s.WidgetProcessId }
    $desktop=New-Desktop
    Wait-For 'Desktop start launches real widget process' { $s=Read-Status; $s -and $s.WidgetProcessId -and (Running $s.WidgetProcessId) }
    $firstWidget=[int](Read-Status).WidgetProcessId
    Wait-For 'Widget is visible to the user after automatic launch' { [FixtureWindows]::Visible($firstWidget) }
    $helper.Kill(); $null=$helper.WaitForExit(3000)
    $helper=Start-Helper
    Wait-For 'Recovered helper reclaims the exact previously owned widget' { $s=Read-Status; $s -and $s.HelperProcessId -eq $helper.Id -and $s.WidgetProcessId -eq $firstWidget -and -not $s.Suppressed }
    $extra=Start-Helper
    Wait-For 'Duplicate helper exits without duplicate widget' { $extra.Refresh(); $extra.HasExited -and @(Get-Process -Name WidgetFixture -ErrorAction SilentlyContinue).Count -eq 1 }
    $manual=Get-Process -Id $firstWidget
    if (-not [FixtureWindows]::Close($firstWidget)) { throw 'Manual WM_CLOSE could not find fixture window' }
    $null=$manual.WaitForExit(3000)
    Wait-For 'Manual close suppresses automatic reopen' { $s=Read-Status; $s -and $s.Suppressed -and -not $s.WidgetProcessId }
    Start-Sleep -Milliseconds 1000
    if (@(Get-Process -Name WidgetFixture -ErrorAction SilentlyContinue).Count) { throw 'Widget reopened after manual close' }
    Write-Output 'PASS: Manual suppression persists'; $passed++
    End-Fixture $desktop; $desktop=New-Desktop
    Wait-For 'New desktop session restores automatic widget' { $s=Read-Status; $s -and $s.WidgetProcessId -and $s.WidgetProcessId -ne $firstWidget }
    $secondWidget=[int](Read-Status).WidgetProcessId
    End-Fixture $desktop; $desktop=$null
    Wait-For 'Desktop exit closes owned widget gracefully' { -not (Running $secondWidget) -and (Read-Status).DesktopStatus -eq 'Stopped' }
    if (-not (Test-Path -LiteralPath (Join-Path $TestDirectory 'graceful-close.txt'))) { throw 'Graceful close marker missing' }
    Set-Content -LiteralPath (Join-Path $TestDirectory 'hidden.fixture') -Value 'hidden'
    Remove-Item -LiteralPath (Join-Path $TestDirectory 'graceful-close.txt') -Force
    $desktop=New-Desktop
    Wait-For 'Hidden main and separate edge fixture launches' { $s=Read-Status; $s -and $s.WidgetProcessId }
    $hiddenWidget=[int](Read-Status).WidgetProcessId
    Start-Sleep -Milliseconds 400
    End-Fixture $desktop; $desktop=$null
    Wait-For 'Edge-hidden main window closes with desktop' { -not (Running $hiddenWidget) -and (Test-Path -LiteralPath (Join-Path $TestDirectory 'graceful-close.txt')) }
    $externalWidget=Start-Process -FilePath $widgetPath -WindowStyle Hidden -PassThru
    $desktop=New-Desktop
    Wait-For 'Already running external widget is not duplicated' { $s=Read-Status; $s -and $s.Suppressed -and -not $s.WidgetProcessId -and @(Get-Process -Name WidgetFixture -ErrorAction SilentlyContinue).Count -eq 1 }
    End-Fixture $desktop; $desktop=$null
    Wait-For 'Desktop exit is observed with external widget present' { (Read-Status).DesktopStatus -eq 'Stopped' }
    if (-not (Running $externalWidget.Id)) { throw 'External widget was incorrectly closed' }
    Write-Output 'PASS: External widget remains untouched'; $passed++
    Set-Content -LiteralPath (Join-Path $stateDir 'stop.request') -Value 'stop'
    Wait-For 'Stop request terminates helper cleanly' { $helper.Refresh(); $helper.HasExited -and (Read-Status).Mode -eq 'Stopped' }
    if ($helper.ExitCode -ne 0) { throw ('Helper failed: '+$helper.ExitCode) }
    $defaultDir=Join-Path $TestDirectory 'default-launch'
    New-Item -ItemType Directory -Path $defaultDir -Force | Out-Null
    Copy-Item -LiteralPath $runner,(Join-Path (Split-Path -Parent $runner) 'CodexLifecycle.psm1'),(Join-Path (Split-Path -Parent $runner) 'FollowTask.psm1') -Destination $defaultDir
    Copy-Item -LiteralPath $widgetPath -Destination (Join-Path $defaultDir 'CodexUsageWidget.exe')
    $stateDir=Join-Path $defaultDir 'state'
    $defaultArgs='-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+(Join-Path $defaultDir 'FollowCodex.ps1')+'" -DesktopExecutablePath "'+$desktopPath+'" -StateDirectory "'+$stateDir+'" -PollIntervalSeconds 0.2'
    $helper=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList $defaultArgs -WindowStyle Hidden -PassThru
    Wait-For 'Packaged helper starts without explicit WidgetPath' { $s=Read-Status; $s -and $s.Mode -eq 'Monitoring' }
    $otherDefaultWidgets=@(Get-Process -Name CodexUsageWidget -ErrorAction SilentlyContinue | Where-Object {$_.Path -ne (Join-Path $defaultDir 'CodexUsageWidget.exe')})
    $desktop=New-Desktop
    $defaultWidgetId=0
    if($otherDefaultWidgets.Count){
        Wait-For 'Default launch respects existing user widget' { $s=Read-Status; $s -and $s.Suppressed -and -not $s.WidgetProcessId }
    } else {
        Wait-For 'Default executable path launches packaged widget' { $s=Read-Status; $s -and $s.WidgetProcessId }
        $defaultWidgetId=[int](Read-Status).WidgetProcessId
    }
    Set-Content -LiteralPath (Join-Path $stateDir 'stop.request') -Value 'stop'
    Wait-For 'Default packaged helper stops its session safely' { $helper.Refresh(); $helper.HasExited -and (-not $defaultWidgetId -or -not (Running $defaultWidgetId)) }
    End-Fixture $desktop; $desktop=$null
    $stateDir=Join-Path $TestDirectory 'disabled-before-start'
    New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $stateDir 'disabled') -Value 'disabled'
    $helper=Start-Helper
    Wait-For 'A pending startup cannot bypass persisted disable' { $helper.Refresh(); $helper.HasExited } 5
    if($helper.ExitCode -ne 0){throw 'Disabled helper must exit cleanly'}
    Write-Output "$passed integration checks passed. Artifacts: $TestDirectory"
} finally {
    if (Test-Path -LiteralPath $stateDir) { Set-Content -LiteralPath (Join-Path $stateDir 'stop.request') -Value 'stop' }
    if ($helper -and -not $helper.HasExited) { $null=$helper.WaitForExit(12000) }
    End-Fixture $desktop; End-Fixture $externalWidget; End-Fixture $extra; End-Fixture $helper
    Get-Process -Name WidgetFixture -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $widgetPath } | Stop-Process -Force
}
