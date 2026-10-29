Set-StrictMode -Version 2.0

function New-FollowState {
    [pscustomobject]@{ SessionIds=@(); HasLaunched=$false; Suppressed=$false; MissingSamples=0 }
}

function Get-CodexObservation {
    param([object[]]$Processes, [string]$DesktopExecutablePath = '')
    $ids = @()
    $uncertain = $false
    foreach ($row in $Processes) {
        $name = [string]$row.Name
        $path = [string]$row.ExecutablePath
        $command = [string]$row.CommandLine
        $namedCandidate = $name -match '^(Codex|ChatGPT)\.exe$'
        $explicit = $DesktopExecutablePath -and $path -and [string]::Equals($path, $DesktopExecutablePath, [StringComparison]::OrdinalIgnoreCase)
        if (-not $namedCandidate -and -not $explicit) { continue }
        if (-not $path) { $uncertain=$true; continue }
        $store = $path -match '\\WindowsApps\\OpenAI\.Codex_[^\\]+\\app\\(?:Codex|ChatGPT)\.exe$'
        if ($DesktopExecutablePath) {
            if (-not $explicit) { continue }
        } elseif (-not $store) { continue }
        if (-not $command) { $uncertain=$true; continue }
        if ($command -match '(?:^|\s)--type(?:=|\s)' -or $command -match '(?:^|\s)app-server(?:\s|$)') { continue }
        if (-not $row.ProcessId -or -not $row.CreationDate) { $uncertain=$true; continue }
        try {
            $created = ([datetime]$row.CreationDate).ToUniversalTime().Ticks
            $ids += ('{0}:{1}' -f $row.ProcessId,$created)
        } catch { $uncertain=$true }
    }
    $status = if ($ids.Count) { 'Running' } elseif ($uncertain) { 'Unknown' } else { 'Stopped' }
    [pscustomobject]@{ Status=$status; SessionIds=@($ids | Sort-Object -Unique) }
}

function Get-NextFollowAction {
    param($State, $Observation, [bool]$WidgetRunning)
    if ($Observation.Status -eq 'Unknown') {
        $State.MissingSamples=0
        return 'None'
    }
    if ($Observation.Status -eq 'Stopped') {
        $State.MissingSamples++
        if ($State.MissingSamples -lt 2) { return 'None' }
        $State.SessionIds=@(); $State.HasLaunched=$false; $State.Suppressed=$false
        if ($WidgetRunning) { return 'Stop' }
        return 'None'
    }
    if ($Observation.Status -ne 'Running' -or -not $Observation.SessionIds.Count) { return 'None' }
    $State.MissingSamples=0
    $overlap = @($State.SessionIds | Where-Object { $Observation.SessionIds -contains $_ })
    if (-not $overlap.Count) { $State.HasLaunched=$false; $State.Suppressed=$false }
    $State.SessionIds=@($Observation.SessionIds)
    if ($WidgetRunning) { $State.HasLaunched=$true; return 'None' }
    if ($State.HasLaunched) { $State.Suppressed=$true }
    if ($State.Suppressed) { return 'None' }
    return 'Start'
}

function Resolve-CodexCliPath {
    param([object[]]$Processes, [string]$InstallRoot, [string]$ExistingPath='')
    if ($ExistingPath -and (Test-Path -LiteralPath $ExistingPath -PathType Leaf)) { return $ExistingPath }
    $root=[IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
    foreach ($row in @($Processes | Sort-Object CreationDate -Descending)) {
        $path=[string]$row.ExecutablePath
        if (-not $path -or [IO.Path]::GetFileName($path) -ine 'codex.exe') { continue }
        try {
            $parent=[IO.Path]::GetDirectoryName([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($path)))
            if ([string]::Equals($parent,$root,[StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $path -PathType Leaf)) { return $path }
        } catch { }
    }
    if (Test-Path -LiteralPath $root -PathType Container) {
        foreach ($directory in @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending)) {
            $candidate=Join-Path $directory.FullName 'codex.exe'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
        }
    }
    return ''
}

Export-ModuleMember -Function New-FollowState,Get-CodexObservation,Get-NextFollowAction,Resolve-CodexCliPath
