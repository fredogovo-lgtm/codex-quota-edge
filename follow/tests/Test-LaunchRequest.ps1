$ErrorActionPreference='Stop'
$root=Join-Path $env:TEMP ('CodexFollowLaunchTest-'+[guid]::NewGuid().ToString('N'))
$unpacked=Join-Path $root 'unpacked'
$local=Join-Path $root 'local'
New-Item -ItemType Directory -Path $unpacked,$local -Force | Out-Null
$oldLocal=$env:LOCALAPPDATA
$oldResult=$env:CODEX_FOLLOW_LAUNCH_TEST_RESULT
try {
    $env:LOCALAPPDATA=$local
    $env:CODEX_FOLLOW_LAUNCH_TEST_RESULT=Join-Path $root 'request.json'
    Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'Launch-FollowCodex.ps1') -Destination $unpacked
    # The scheduler boundary is a stub here; actual independent execution and
    # ownership rejection are exercised by Test-TaskHost.ps1.
    [IO.File]::WriteAllText((Join-Path $unpacked 'FollowTask.psm1'), @'
function Start-FollowTask([string]$InstallDirectory,[string]$StateDirectory){
    [IO.File]::WriteAllText($env:CODEX_FOLLOW_LAUNCH_TEST_RESULT,(@{Install=$InstallDirectory;State=$StateDirectory}|ConvertTo-Json))
}
function Get-FollowStateDirectory{return (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) '.codex-usage-widget\follow')}
Export-ModuleMember -Function Start-FollowTask,Get-FollowStateDirectory
'@,(New-Object Text.UTF8Encoding($true)))
    & (Join-Path $unpacked 'Launch-FollowCodex.ps1')
    $request=Get-Content -LiteralPath $env:CODEX_FOLLOW_LAUNCH_TEST_RESULT -Raw | ConvertFrom-Json
    if($request.Install -cne (Join-Path $local 'Programs\CodexUsageWidget-Follow')){throw 'FAIL: unpacked Start launcher requests its package folder instead of the installed task'}
    $expectedState=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) '.codex-usage-widget\follow'
    if($request.State -cne $expectedState){throw 'FAIL: launch state must be shared Profile state, outside MSIX AppData redirection'}
    Write-Output 'PASS: unpacked launcher requests the canonical installed task and state'
} finally {
    $env:LOCALAPPDATA=$oldLocal
    $env:CODEX_FOLLOW_LAUNCH_TEST_RESULT=$oldResult
}
