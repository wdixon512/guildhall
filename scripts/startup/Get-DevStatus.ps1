#Requires -Version 7.0
[CmdletBinding()]
param([switch]$Json)
. "$PSScriptRoot/../misc/Common.ps1"

$currentFingerprint = Get-SourceFingerprint
$sessionRoot = Join-Path $LocalDirectory 'dev-sessions'
$sessions = if (Test-Path -LiteralPath $sessionRoot) { Get-ChildItem -LiteralPath $sessionRoot -Directory } else { @() }
$sessionReports = @()

foreach ($directory in $sessions) {
    $manifestPath = Join-Path $directory.FullName 'session.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { continue }
    $session = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([IO.Path]::GetFullPath($session.checkout) -ne [IO.Path]::GetFullPath($RepoRoot)) { continue }

    $processes = foreach ($key in @('launcherPid','hostLauncherPid','frontendPid','desktopPid')) {
        $processId = $session.$key
        if ($null -eq $processId) { continue }
        $process = Get-Process -Id ([int]$processId) -ErrorAction SilentlyContinue
        [pscustomobject]@{ Role = $key; PID = [int]$processId; State = if ($process) { 'running' } else { 'exited' } }
    }
    $binaryPath = Join-Path $RepoRoot 'apps/desktop/src-tauri/target/debug/guildhall-desktop.exe'
    $binaryFingerprint = if (Test-Path -LiteralPath $binaryPath) { (Get-FileHash -LiteralPath $binaryPath -Algorithm SHA256).Hash } else { 'missing' }
    $binaryIdentity = if ($session.desktopBinarySha256AtStart) {
        if ($binaryFingerprint -eq $session.desktopBinarySha256AtStart) { 'matches launch' } else { 'on-disk binary differs from launch' }
    } else { 'not an embedded build' }
    $workingTree = @(& git -C $RepoRoot status --porcelain --untracked-files=normal 2>$null)
    $gitState = if ($LASTEXITCODE -eq 0) { if ($workingTree.Count) { "dirty ($($workingTree.Count) entries)" } else { 'clean' } } else { 'git status unavailable' }
    $statusIsFresh = $false
    try { $statusIsFresh = ([datetime]::UtcNow - [datetime]::Parse($session.statusCheckedUtc).ToUniversalTime()).TotalSeconds -lt 15 } catch { }
    $sessionReports += [pscustomobject]@{
        Session = $session.sessionId
        Mode = $session.mode
        Checkout = $session.checkout
        Ports = if ($session.frontendPort) { "host=$($session.hostPort), frontend=$($session.frontendPort)" } else { "host=$($session.hostPort)" }
        StartedUtc = $session.startedUtc
        Host = if ($statusIsFresh) { $session.hostStatus } else { 'stale status snapshot' }
        SQL = if ($statusIsFresh) { $session.sqlStatus } else { 'stale status snapshot' }
        StatusCheckedUtc = $session.statusCheckedUtc
        WorkingTree = $gitState
        SourceFingerprintAtStart = $session.sourceFingerprintAtStart
        CurrentSourceFingerprint = $currentFingerprint
        DesktopBinary = $binaryIdentity
        Processes = @($processes)
    }
}

$listenerSummary = foreach ($port in @(43120,1420)) {
    $listeners = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue
    foreach ($listener in $listeners) {
        $process = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
        $path = if ($process) { try { $process.Path } catch { $null } } else { $null }
        $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId=$($listener.OwningProcess)" -ErrorAction SilentlyContinue
        $expectedVite = [regex]::Escape((Join-Path $RepoRoot 'apps/desktop/node_modules/vite/bin/vite.js'))
        $isGuildhall = ($path -and $path.StartsWith(($RepoRoot.TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase)) -or
            ($processInfo.CommandLine -and $processInfo.CommandLine -match $expectedVite)
        $ownership = if ($isGuildhall) { 'Guildhall checkout' } else { 'unverified owner; left untouched' }
        [pscustomobject]@{ Port = $port; PID = $listener.OwningProcess; Process = if ($process) { $process.ProcessName } else { 'exited' }; Ownership = $ownership }
    }
}
$listenerSummary = @($listenerSummary)

if ($Json) {
    [pscustomobject]@{
        Sessions = $sessionReports
        Listeners = $listenerSummary
        Source = [pscustomobject]@{
            Checkout = $RepoRoot
            CurrentSourceFingerprint = $currentFingerprint
            WorkingTree = if (@(& git -C $RepoRoot status --porcelain --untracked-files=normal 2>$null).Count) { 'dirty' } else { 'clean' }
        }
    } | ConvertTo-Json -Depth 8
    return
}

if (-not $sessionReports.Count) { Write-Output 'No Guildhall dev session is registered.' }
foreach ($report in $sessionReports) {
    [pscustomobject]@{
        Session = $report.Session
        Mode = $report.Mode
        Checkout = $report.Checkout
        Ports = $report.Ports
        StartedUtc = $report.StartedUtc
        Host = $report.Host
        SQL = $report.SQL
        StatusCheckedUtc = $report.StatusCheckedUtc
        WorkingTree = $report.WorkingTree
        SourceFingerprintAtStart = $report.SourceFingerprintAtStart
        CurrentSourceFingerprint = $report.CurrentSourceFingerprint
        DesktopBinary = $report.DesktopBinary
        Processes = ($report.Processes | ForEach-Object { "$($_.Role)=$($_.PID):$($_.State)" }) -join ', '
    } | Format-List
}
$listenerSummary | Format-Table -AutoSize
