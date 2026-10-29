[CmdletBinding()]
param(
    [string]$WidgetPath = '',
    [string]$DesktopExecutablePath = '',
    [string]$StateDirectory = '',
    [ValidateRange(0.1,60)][double]$PollIntervalSeconds = 2
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'CodexLifecycle.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'FollowTask.psm1') -Force -DisableNameChecking
if(-not $StateDirectory){$StateDirectory=Get-FollowStateDirectory}
if (-not $WidgetPath) { $WidgetPath=Join-Path $PSScriptRoot 'CodexUsageWidget.exe' }
$WidgetPath = [IO.Path]::GetFullPath($WidgetPath)
if (-not (Test-Path -LiteralPath $WidgetPath -PathType Leaf)) { throw 'Widget executable not found.' }
if ($DesktopExecutablePath) { $DesktopExecutablePath=[IO.Path]::GetFullPath($DesktopExecutablePath) }
$StateDirectory=[IO.Path]::GetFullPath($StateDirectory)
$disabledMarker=Join-Path $StateDirectory 'disabled'
if (Test-Path -LiteralPath $disabledMarker) { exit 0 }
$sha=[Security.Cryptography.SHA256]::Create()
try { $hash=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($StateDirectory.ToLowerInvariant())))).Replace('-','').Substring(0,16) }
finally { $sha.Dispose() }
$mutex=New-Object Threading.Mutex($false, ('Local\CodexUsageWidget-Follow-'+$hash))
$locked=$false
try { $locked=$mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked=$true }
if (-not $locked) { $mutex.Dispose(); exit 0 }
New-Item -ItemType Directory -Path $StateDirectory -Force | Out-Null
$stopRequest=Join-Path $StateDirectory 'stop.request'
$statusPath=Join-Path $StateDirectory 'follow-status.json'
$logPath=Join-Path $StateDirectory 'follow.log'
$owned=$null; $ownedStart=0L; $state=New-FollowState
$lastError=''; $nextLaunch=[datetime]::MinValue; $exitCode=0
$configuredCliPath=$env:CODEX_PATH

function Log([string]$Message) {
    try {
        if ((Test-Path -LiteralPath $logPath) -and (Get-Item -LiteralPath $logPath).Length -gt 262144) {
            Move-Item -LiteralPath $logPath -Destination ($logPath+'.previous') -Force
        }
        Add-Content -LiteralPath $logPath -Encoding UTF8 -Value (([datetime]::UtcNow.ToString('o'))+' '+$Message)
    } catch { }
}
function Owned-Running {
    if ($null -eq $script:owned) { return $false }
    try {
        $script:owned.Refresh()
        return (-not $script:owned.HasExited -and $script:owned.StartTime.ToUniversalTime().Ticks -eq $script:ownedStart)
    } catch { return $false }
}
function Save-Status([string]$DesktopStatus, [string]$Mode='Monitoring') {
    try {
        $widgetId=if (Owned-Running) { $script:owned.Id } else { $null }
        $status=[ordered]@{
            Mode=$Mode; HelperProcessId=$PID; HelperStartedUtc=$script:helperStarted;
            DesktopStatus=$DesktopStatus; WidgetProcessId=$widgetId;
            WidgetStartedUtc=if($widgetId){$script:owned.StartTime.ToUniversalTime().ToString('o')}else{$null};
            DesktopSessionIds=@($script:state.SessionIds); HasLaunched=$script:state.HasLaunched;
            Suppressed=$script:state.Suppressed; LastError=$script:lastError;
            ObservedUtc=[datetime]::UtcNow.ToString('o')
        }
        $temp=$statusPath+'.'+$PID+'.tmp'
        [IO.File]::WriteAllText($temp,($status | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
        Move-Item -LiteralPath $temp -Destination $statusPath -Force
    } catch { }
}

# EnumWindows includes hidden main windows: an edge-hidden widget must close its
# WPF main window, not just the separate visible edge tab.
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class CodexFollowWindows {
    private delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam);
    public static bool CloseWidget(int pid) {
        bool sent=false;
        EnumWindows(delegate(IntPtr h, IntPtr ignored) {
            uint owner; GetWindowThreadProcessId(h, out owner);
            if (owner != (uint)pid) return true;
            var title=new StringBuilder(512); GetWindowText(h,title,title.Capacity);
            if (title.ToString()=="Codex \u7528\u91cf\u60ac\u6d6e\u7a97") {
                sent=PostMessage(h,0x0010,IntPtr.Zero,IntPtr.Zero);
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return sent;
    }
}
'@
function Stop-OwnedWidget {
    if (-not (Owned-Running)) { return }
    try {
        if (-not [CodexFollowWindows]::CloseWidget($script:owned.Id)) { $null=$script:owned.CloseMainWindow() }
        if (-not $script:owned.WaitForExit(7000) -and (Owned-Running)) {
            # Force only the exact Process object created by this helper; never by name.
            $script:owned.Kill(); $null=$script:owned.WaitForExit(3000)
            Log 'Owned widget required forced close.'
        } else { Log 'Owned widget closed gracefully.' }
    } catch { Log 'Owned widget close failed.'; $script:lastError='WidgetCloseFailed' }
}

try {
    $script:helperStarted=(Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('o')
    Log 'Follow helper started.'
    # A recovery trigger may follow an abrupt helper termination. Only reclaim
    # an earlier owned widget when PID, start time and executable all match.
    # The per-state mutex is already held, so another helper cannot own it now.
    $previous=$null
    if(Test-Path -LiteralPath $statusPath){
        try{$previous=Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json}catch{}
    }
    if($previous -and $previous.Mode -eq 'Monitoring'){
        if($previous.DesktopSessionIds){
            $state.SessionIds=@($previous.DesktopSessionIds)
            $state.HasLaunched=[bool]$previous.HasLaunched
            $state.Suppressed=[bool]$previous.Suppressed
        }
        if($previous.WidgetProcessId -and $previous.WidgetStartedUtc){
            $candidate=Get-Process -Id $previous.WidgetProcessId -ErrorAction SilentlyContinue
            if($candidate){
                try{
                    $expectedStart=([datetime]::Parse($previous.WidgetStartedUtc)).ToUniversalTime().Ticks
                    if($candidate.StartTime.ToUniversalTime().Ticks -eq $expectedStart -and [string]::Equals($candidate.Path,$WidgetPath,[StringComparison]::OrdinalIgnoreCase)){
                        $owned=$candidate; $ownedStart=$expectedStart
                        Log 'Previously owned widget recovered with matching identity.'
                    }else{$candidate.Dispose()}
                }catch{$candidate.Dispose()}
            }
        }
    }
    $filter="Name='Codex.exe' OR Name='ChatGPT.exe'"
    if ($DesktopExecutablePath) {
        $customName=[IO.Path]::GetFileName($DesktopExecutablePath).Replace("'","''")
        $filter += " OR Name='$customName'"
    }
    while (-not (Test-Path -LiteralPath $stopRequest) -and -not (Test-Path -LiteralPath $disabledMarker)) {
        try {
            $rows=@(Get-CimInstance Win32_Process -Filter $filter -ErrorAction Stop)
            $observation=Get-CodexObservation -Processes $rows -DesktopExecutablePath $DesktopExecutablePath
            $lastError=''
        } catch {
            $observation=[pscustomobject]@{Status='Unknown';SessionIds=@()}
            $lastError='ProcessQueryFailed'
        }
        $action=Get-NextFollowAction $state $observation (Owned-Running)
        if ($action -eq 'Stop') { Stop-OwnedWidget }
        if ($action -eq 'Start' -and [datetime]::UtcNow -ge $nextLaunch -and -not (Test-Path -LiteralPath $disabledMarker) -and -not (Test-Path -LiteralPath $stopRequest)) {
            $widgetName=[IO.Path]::GetFileNameWithoutExtension($WidgetPath)
            # The upstream mutex is global for the app. Respect any existing copy;
            # do not launch a duplicate that would show a modal error dialog.
            $existing=@(Get-Process -Name $widgetName -ErrorAction SilentlyContinue)
            if ($existing.Count) {
                $state.HasLaunched=$true; $state.Suppressed=$true
                Log 'Existing widget left under external ownership; session suppressed.'
            } else {
                try {
                    # Upstream understands CODEX_PATH. New Codex Desktop releases
                    # keep their CLI in LocalAppData rather than app/resources.
                    # Change only this helper's child environment, never user settings.
                    if (-not $DesktopExecutablePath) {
                        $cli=Resolve-CodexCliPath -Processes $rows -InstallRoot (Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin') -ExistingPath $configuredCliPath
                        if ($cli) { $env:CODEX_PATH=$cli }
                    }
                    $owned=Start-Process -FilePath $WidgetPath -WorkingDirectory (Split-Path -Parent $WidgetPath) -WindowStyle Normal -PassThru
                    $ownedStart=$owned.StartTime.ToUniversalTime().Ticks
                    $state.HasLaunched=$true
                    Log 'Owned widget launched.'
                } catch {
                    $lastError='WidgetLaunchFailed'; $nextLaunch=[datetime]::UtcNow.AddSeconds(30)
                    Log 'Widget launch failed; retry delayed.'
                }
            }
        }
        Save-Status $observation.Status
        Start-Sleep -Milliseconds ([int]($PollIntervalSeconds*1000))
    }
} catch {
    $exitCode=1; $lastError='HelperFailed'; Log ('Helper failed: '+$_.Exception.GetType().Name)
} finally {
    Stop-OwnedWidget
    Save-Status 'Unknown' 'Stopped'
    if ($null -ne $owned) { $owned.Dispose() }
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
exit $exitCode
