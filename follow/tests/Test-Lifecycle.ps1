$ErrorActionPreference = 'Stop'
$module = Join-Path (Split-Path -Parent $PSScriptRoot) 'CodexLifecycle.psm1'
if (-not (Test-Path -LiteralPath $module)) {
    Write-Output 'FAIL: Codex lifecycle feature has not been implemented.'
    exit 1
}
Import-Module $module -Force -DisableNameChecking
$script:passed = 0
function Check([string]$Name, [scriptblock]$Body) {
    try { & $Body; $script:passed++; Write-Output "PASS: $Name" }
    catch { Write-Output "FAIL: $Name - $($_.Exception.Message)"; exit 1 }
}
function Equal($Actual, $Expected) {
    if ($Actual -cne $Expected) { throw "expected [$Expected], got [$Actual]" }
}
function Observation([string]$Status, [string[]]$Ids = @()) {
    [pscustomobject]@{ Status = $Status; SessionIds = $Ids }
}
function ProcessRow([string]$Name, [string]$Path, [string]$Command = 'desktop', [int]$Id = 10) {
    [pscustomobject]@{ Name=$Name; ExecutablePath=$Path; CommandLine=$Command; ProcessId=$Id; CreationDate=[datetime]'2026-10-29T01:00:00Z' }
}
$store = 'C:\Program Files\WindowsApps\OpenAI.Codex_26.1_x64__abc\app\'
Check 'Renamed Store ChatGPT desktop is recognized' {
    $o = Get-CodexObservation -Processes @(ProcessRow 'ChatGPT.exe' ($store+'ChatGPT.exe'))
    Equal $o.Status 'Running'; Equal $o.SessionIds.Count 1
}
Check 'Older Store Codex desktop is recognized' {
    Equal (Get-CodexObservation -Processes @(ProcessRow 'Codex.exe' ($store+'Codex.exe'))).Status 'Running'
}
Check 'Electron renderer does not keep desktop alive' {
    Equal (Get-CodexObservation -Processes @(ProcessRow 'ChatGPT.exe' ($store+'ChatGPT.exe') '--type=renderer')).Status 'Stopped'
}
Check 'CLI and app-server cannot start the widget' {
    $cli='C:\Users\Someone\AppData\Local\OpenAI\Codex\bin\hash\codex.exe'
    Equal (Get-CodexObservation -Processes @((ProcessRow 'codex.exe' $cli 'codex app-server --stdio'), (ProcessRow 'codex.exe' $cli 'codex exec task' 11))).Status 'Stopped'
}
Check 'Unrelated ChatGPT installation is ignored' {
    Equal (Get-CodexObservation -Processes @(ProcessRow 'ChatGPT.exe' 'C:\Other\ChatGPT.exe')).Status 'Stopped'
}
Check 'Explicit portable desktop path is recognized case insensitively' {
    Equal (Get-CodexObservation -Processes @(ProcessRow 'Portable.exe' 'D:\My App\Portable.exe') -DesktopExecutablePath 'd:\my app\portable.exe').Status 'Running'
}
Check 'Explicit desktop selection ignores other Codex installations' {
    Equal (Get-CodexObservation -Processes @(ProcessRow 'ChatGPT.exe' ($store+'ChatGPT.exe')) -DesktopExecutablePath 'D:\My App\Portable.exe').Status 'Stopped'
}
Check 'Inaccessible candidate returns Unknown instead of Stopped' {
    Equal (Get-CodexObservation -Processes @(ProcessRow 'ChatGPT.exe' $null $null)).Status 'Unknown'
}
Check 'Missing command line for matching desktop is Unknown' {
    Equal (Get-CodexObservation -Processes @(ProcessRow 'ChatGPT.exe' ($store+'ChatGPT.exe') $null)).Status 'Unknown'
}
Check 'No desktop produces no launch' {
    $s=New-FollowState; Equal (Get-NextFollowAction $s (Observation 'Stopped') $false) 'None'
}
Check 'Desktop appearance requests one widget launch' {
    $s=New-FollowState; Equal (Get-NextFollowAction $s (Observation 'Running' @('10:start')) $false) 'Start'
    $s.HasLaunched=$true
    Equal (Get-NextFollowAction $s (Observation 'Running' @('10:start')) $true) 'None'
}
Check 'Manual close suppresses relaunch in current session' {
    $s=New-FollowState; $null=Get-NextFollowAction $s (Observation 'Running' @('10:start')) $false
    $s.HasLaunched=$true
    Equal (Get-NextFollowAction $s (Observation 'Running' @('10:start')) $false) 'None'
    Equal $s.Suppressed $true
    Equal (Get-NextFollowAction $s (Observation 'Running' @('10:start')) $false) 'None'
}
Check 'New desktop identity clears manual suppression even without empty sample' {
    $s=New-FollowState; $null=Get-NextFollowAction $s (Observation 'Running' @('10:start')) $false
    $s.HasLaunched=$true; $null=Get-NextFollowAction $s (Observation 'Running' @('10:start')) $false
    Equal (Get-NextFollowAction $s (Observation 'Running' @('11:new')) $false) 'Start'
}
Check 'Additional desktop window does not clear manual suppression' {
    $s=New-FollowState; $null=Get-NextFollowAction $s (Observation 'Running' @('10:start')) $false
    $s.HasLaunched=$true; $null=Get-NextFollowAction $s (Observation 'Running' @('10:start')) $false
    Equal (Get-NextFollowAction $s (Observation 'Running' @('10:start','11:new')) $false) 'None'
}
Check 'Two confirmed empty samples close owned widget' {
    $s=New-FollowState; $null=Get-NextFollowAction $s (Observation 'Running' @('10:start')) $true
    Equal (Get-NextFollowAction $s (Observation 'Stopped') $true) 'None'
    Equal (Get-NextFollowAction $s (Observation 'Stopped') $true) 'Stop'
}
Check 'Unknown query never closes a running widget or changes session' {
    $s=New-FollowState; $null=Get-NextFollowAction $s (Observation 'Running' @('10:start')) $true
    $null=Get-NextFollowAction $s (Observation 'Stopped') $true
    Equal (Get-NextFollowAction $s (Observation 'Unknown') $true) 'None'
    Equal $s.SessionIds[0] '10:start'
    Equal (Get-NextFollowAction $s (Observation 'Stopped') $true) 'None'
}
Check 'Rapid desktop restart cancels pending close' {
    $s=New-FollowState; $null=Get-NextFollowAction $s (Observation 'Running' @('10:start')) $true
    $null=Get-NextFollowAction $s (Observation 'Stopped') $true
    Equal (Get-NextFollowAction $s (Observation 'Running' @('11:new')) $true) 'None'
    Equal (Get-NextFollowAction $s (Observation 'Stopped') $true) 'None'
}
Check 'Clean new session reopens after a complete exit' {
    $s=New-FollowState; $null=Get-NextFollowAction $s (Observation 'Running' @('10:start')) $true
    $null=Get-NextFollowAction $s (Observation 'Stopped') $true
    $null=Get-NextFollowAction $s (Observation 'Stopped') $true
    Equal (Get-NextFollowAction $s (Observation 'Running' @('11:new')) $false) 'Start'
}
Check 'Current installed CLI path is passed to upstream without changing settings' {
    $root=Join-Path $env:TEMP ('CodexCliResolver-'+[guid]::NewGuid().ToString('N'))
    $a=Join-Path $root 'version-a'; $b=Join-Path $root 'version-b'
    New-Item -ItemType Directory -Path $a,$b -Force | Out-Null
    try {
        $active=Join-Path $a 'codex.exe'; $other=Join-Path $b 'codex.exe'
        [IO.File]::WriteAllText($active,'fixture'); [IO.File]::WriteAllText($other,'fixture')
        Equal (Resolve-CodexCliPath -Processes @(ProcessRow 'codex.exe' $active 'codex app-server --stdio') -InstallRoot $root) $active
        Equal (Resolve-CodexCliPath -Processes @() -InstallRoot $root -ExistingPath $other) $other
        Equal (Resolve-CodexCliPath -Processes @() -InstallRoot (Join-Path $root 'missing')) ''
    } finally { Remove-Item -LiteralPath $active,$other -Force; [IO.Directory]::Delete($a); [IO.Directory]::Delete($b); [IO.Directory]::Delete($root) }
}
Write-Output "$script:passed lifecycle tests passed."
