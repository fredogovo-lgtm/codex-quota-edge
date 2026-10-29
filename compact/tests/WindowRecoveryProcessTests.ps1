#requires -Version 7.0
[CmdletBinding()]
param(
 [string]$ExecutablePath=(Join-Path $PSScriptRoot '../../work/publish-v2.2.2/CodexUsageWidget.exe'),
 [string]$WorkRoot=(Join-Path $PSScriptRoot '../../work/window-recovery-process')
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$ExecutablePath=[IO.Path]::GetFullPath($ExecutablePath)
$WorkRoot=[IO.Path]::GetFullPath($WorkRoot)
if(-not [OperatingSystem]::IsWindows()){throw 'This isolated GUI process test requires Windows.'}
if(-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)){throw "Published widget executable not found: $ExecutablePath"}
if(-not ('WindowRecoveryProcessProbe' -as [type])) {
 Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class WindowRecoveryProcessProbe {
 private delegate bool Callback(IntPtr window,IntPtr state);
 [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback,IntPtr state);
 [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window,int command);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
 [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window,out Rect rect);
 [StructLayout(LayoutKind.Sequential)] private struct Rect { public int left,top,right,bottom; }
 public static IntPtr Find(int pid) {
  IntPtr found=IntPtr.Zero;
  EnumWindows(delegate(IntPtr window,IntPtr ignored) {
   uint owner;GetWindowThreadProcessId(window,out owner);if(owner!=(uint)pid)return true;
   var title=new StringBuilder(100);GetWindowText(window,title,title.Capacity);
   if(title.ToString()=="Codex \u7528\u91cf\u60ac\u6d6e\u7a97"){found=window;return false;}return true;
  },IntPtr.Zero);return found;
 }
 public static bool Topmost(IntPtr window){return (GetWindowLongPtr(window,-20).ToInt64()&8)!=0;}
 public static string Bounds(IntPtr window){Rect r;if(!GetWindowRect(window,out r))return "unavailable";return r.left+","+r.top+","+r.right+","+r.bottom;}
}
'@
}

$previewMutex='Local\CodexUsageWidget-87D45B32-BCF8-44E5-8F9B-36E9BB488F4F-Preview'
$probeMutex=$null
if([Threading.Mutex]::TryOpenExisting($previewMutex,[ref]$probeMutex)){
 $probeMutex.Dispose();throw 'An existing demo/preview owns the preview mutex. No process or window was changed.'
}
$runDirectory=Join-Path $WorkRoot ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$settings=Join-Path $runDirectory 'settings.json'
$seed=[ordered]@{style='line';dock=[ordered]@{edge='left';fraction=0.31;monitor=$null;locked=$true};topmost=$false}
[IO.File]::WriteAllText($settings,($seed|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
$first=$null;$second=$null;$firstStartTicks=0L;$window=[IntPtr]::Zero;$checks=0
$evidence=[ordered]@{status='FAIL';checks=0;version=[Diagnostics.FileVersionInfo]::GetVersionInfo($ExecutablePath).ProductVersion;firstPid=$null;secondPid=$null;productionMessageIgnored=$false;previewMessageRecovered=$false;duplicateExited=$false;firstPidPreserved=$false;topmostRemainedDisabled=$false;preferencesPreserved=$false;boundsPreserved=$false;foregroundPreserved=$false;gracefulCleanup=$false}
function Assert-Condition([bool]$Condition,[string]$Name) {
 if(-not $Condition){throw $Name};$script:checks++
}
function Wait-Condition([scriptblock]$Condition,[string]$Name,[int]$Milliseconds=8000) {
 $deadline=[datetime]::UtcNow.AddMilliseconds($Milliseconds)
 do {if(& $Condition){return};Start-Sleep -Milliseconds 75}while([datetime]::UtcNow -lt $deadline)
 throw "Timeout: $Name"
}
function Start-Demo {
 $start=[Diagnostics.ProcessStartInfo]::new($ExecutablePath)
 $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WorkingDirectory=[IO.Path]::GetDirectoryName($ExecutablePath)
 $start.ArgumentList.Add('--demo');$start.ArgumentList.Add('--settings-path');$start.ArgumentList.Add($settings)
 return [Diagnostics.Process]::Start($start)
}
function Same-Preferences {
 $value=[IO.File]::ReadAllText($settings)|ConvertFrom-Json
 return ($value.style -eq 'line' -and $value.dock.edge -eq 'left' -and [Math]::Abs([double]$value.dock.fraction-0.31) -lt 0.00000001 -and $value.dock.locked -eq $true -and $value.topmost -eq $false)
}
function Own-First-Process {
 if($null -eq $first -or $first.HasExited){return $false}
 return ([string]::Equals([IO.Path]::GetFullPath($first.MainModule.FileName),$ExecutablePath,[StringComparison]::OrdinalIgnoreCase) -and $first.StartTime.ToUniversalTime().Ticks -eq $firstStartTicks)
}
try {
 $first=Start-Demo;$firstStartTicks=$first.StartTime.ToUniversalTime().Ticks;$evidence.firstPid=$first.Id
 Wait-Condition {if($first.HasExited){throw 'First isolated demo exited before creating its window.'};$script:window=[WindowRecoveryProcessProbe]::Find($first.Id);$script:window -ne [IntPtr]::Zero -and [WindowRecoveryProcessProbe]::IsWindowVisible($script:window)} 'first isolated demo appears'
 Assert-Condition (Own-First-Process) 'Started process identity differs from isolated published executable.'
 Assert-Condition (-not [WindowRecoveryProcessProbe]::Topmost($window)) 'Initial topmost=false was ignored.'
 Assert-Condition (Same-Preferences) 'Initial preferences changed unexpectedly.'
 $bounds=[WindowRecoveryProcessProbe]::Bounds($window)
 $productionMessage=[WindowRecoveryProcessProbe]::RegisterWindowMessage('CodexQuotaEdge.RestorePanel.87D45B32-BCF8-44E5-8F9B-36E9BB488F4F')
 $previewMessage=[WindowRecoveryProcessProbe]::RegisterWindowMessage('CodexQuotaEdge.RestorePanel.Preview.87D45B32-BCF8-44E5-8F9B-36E9BB488F4F')
 Assert-Condition ($productionMessage -ge 0xC000 -and $previewMessage -ge 0xC000 -and $productionMessage -ne $previewMessage) 'Mode-specific registered messages are unavailable or equal.'
 $foreground=[WindowRecoveryProcessProbe]::GetForegroundWindow()
 $null=[WindowRecoveryProcessProbe]::ShowWindow($window,0)
 Assert-Condition (-not [WindowRecoveryProcessProbe]::IsWindowVisible($window)) 'Could not hide isolated demo window.'
 Assert-Condition ([WindowRecoveryProcessProbe]::PostMessage($window,$productionMessage,[IntPtr]::Zero,[IntPtr]::Zero)) 'Could not send production message to isolated demo.'
 Start-Sleep -Milliseconds 3400
 $evidence.productionMessageIgnored=-not [WindowRecoveryProcessProbe]::IsWindowVisible($window)
 Assert-Condition $evidence.productionMessageIgnored 'Production message or disabled watchdog incorrectly restored the demo.'
 Assert-Condition ([WindowRecoveryProcessProbe]::PostMessage($window,$previewMessage,[IntPtr]::Zero,[IntPtr]::Zero)) 'Could not send preview recovery message.'
 Wait-Condition {[WindowRecoveryProcessProbe]::IsWindowVisible($window)} 'preview message restores isolated demo' 3000
 $evidence.previewMessageRecovered=$true
 Assert-Condition (-not [WindowRecoveryProcessProbe]::Topmost($window)) 'Preview message unexpectedly enabled topmost.'
 Assert-Condition ([WindowRecoveryProcessProbe]::GetForegroundWindow() -eq $foreground) 'Mode-specific recovery activated a foreground window.'
 $null=[WindowRecoveryProcessProbe]::ShowWindow($window,0)
 Assert-Condition (-not [WindowRecoveryProcessProbe]::IsWindowVisible($window)) 'Could not hide demo before repeat-launch test.'
 $foreground=[WindowRecoveryProcessProbe]::GetForegroundWindow()
 $second=Start-Demo;$evidence.secondPid=$second.Id
 $evidence.duplicateExited=$second.WaitForExit(5000)
 Assert-Condition $evidence.duplicateExited 'Second launch did not exit promptly.'
 Assert-Condition ($second.ExitCode -eq 0) 'Second launch returned an error.'
 Wait-Condition {[WindowRecoveryProcessProbe]::IsWindowVisible($window)} 'second launch finds original hidden window' 3000
 $evidence.firstPidPreserved=(Own-First-Process) -and [WindowRecoveryProcessProbe]::Find($first.Id) -eq $window
 $evidence.topmostRemainedDisabled=-not [WindowRecoveryProcessProbe]::Topmost($window)
 $evidence.preferencesPreserved=Same-Preferences
 $evidence.boundsPreserved=[WindowRecoveryProcessProbe]::Bounds($window) -eq $bounds
 $evidence.foregroundPreserved=[WindowRecoveryProcessProbe]::GetForegroundWindow() -eq $foreground
 Assert-Condition $evidence.firstPidPreserved 'Repeat launch replaced the first process or its window.'
 Assert-Condition $evidence.topmostRemainedDisabled 'Repeat launch unexpectedly enabled topmost.'
 Assert-Condition $evidence.preferencesPreserved 'Repeat launch changed style position lock or topmost preference.'
 Assert-Condition $evidence.boundsPreserved 'Repeat launch moved or resized the original panel.'
 Assert-Condition $evidence.foregroundPreserved 'Repeat launch activated a foreground window.'
 $evidence.status='PASS'
} catch {
 $evidence.error=$_.Exception.Message
} finally {
 if($null -ne $first -and -not $first.HasExited){
  if(Own-First-Process){
   $ownedWindow=[WindowRecoveryProcessProbe]::Find($first.Id)
   if($ownedWindow -ne [IntPtr]::Zero){$null=[WindowRecoveryProcessProbe]::PostMessage($ownedWindow,0x10,[IntPtr]::Zero,[IntPtr]::Zero)}
   $evidence.gracefulCleanup=$first.WaitForExit(5000)
   if(-not $evidence.gracefulCleanup){
    $evidence.status='FAIL';$evidence.error='Isolated demo did not close gracefully; its verified owned process was terminated.'
    if(Own-First-Process){$first.Kill();$null=$first.WaitForExit(3000)}
   }
  }else{$evidence.status='FAIL';$evidence.error='Test process identity could not be verified for cleanup; no other process was touched.'}
 }elseif($null -ne $first){$evidence.gracefulCleanup=$true}
 if($null -ne $second){
  if(-not $second.HasExited){
   # Only a process created by this invocation may be terminated after a failed duplicate-launch check.
   if([string]::Equals([IO.Path]::GetFullPath($second.MainModule.FileName),$ExecutablePath,[StringComparison]::OrdinalIgnoreCase)){$second.Kill();$null=$second.WaitForExit(3000)}
  };$second.Dispose()
 }
 if($null -ne $first){$first.Dispose()}
 $evidence.checks=$checks
 $json=$evidence|ConvertTo-Json -Depth 4
 [IO.File]::WriteAllText((Join-Path $runDirectory 'evidence.json'),$json,[Text.UTF8Encoding]::new($false))
 Write-Output $json
}
if($evidence.status -ne 'PASS'){exit 1}
