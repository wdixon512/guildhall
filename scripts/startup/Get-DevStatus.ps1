#Requires -Version 7.0
[CmdletBinding()]
param([switch]$Json, [switch]$CurrentCheckoutOnly)
. "$PSScriptRoot/../misc/Common.ps1"

$currentFingerprint = Get-SourceFingerprint
$entries = Get-RegisteredDevSessions
if ($CurrentCheckoutOnly) {
    $entries = @($entries | Where-Object { [IO.Path]::GetFullPath($_.Session.checkout) -eq [IO.Path]::GetFullPath($RepoRoot) })
}
$sessionReports = @()
$portsToInspect = [System.Collections.Generic.List[int]]::new()
$portsToInspect.Add(43120)
$portsToInspect.Add(1420)

foreach ($entry in $entries) {
    $session = $entry.Session
    if ($session.hostPort) { $portsToInspect.Add([int]$session.hostPort) }
    if ($session.frontendPort) { $portsToInspect.Add([int]$session.frontendPort) }
    $checkoutRoot = [string]$session.checkout
    $binaryPath = Join-Path $checkoutRoot 'apps/desktop/src-tauri/target/debug/guildhall-desktop.exe'
    $binaryFingerprint = if (Test-SafePath $binaryPath) { (Get-FileHash -LiteralPath $binaryPath -Algorithm SHA256).Hash } else { 'missing' }
    $binaryIdentity = if ($session.desktopBinarySha256AtStart) {
        if ($binaryFingerprint -eq $session.desktopBinarySha256AtStart) { 'matches launch' } else { 'on-disk binary differs from launch' }
    } else { 'not an embedded build' }
    $workingTree = @(& git -C $checkoutRoot status --porcelain --untracked-files=normal 2>$null)
    $gitState = if ($LASTEXITCODE -eq 0) { if ($workingTree.Count) { "dirty ($($workingTree.Count) entries)" } else { 'clean' } } else { 'git status unavailable' }
    $statusIsFresh = $false
    try { $statusIsFresh = ([datetime]::UtcNow - [datetime]::Parse($session.statusCheckedUtc).ToUniversalTime()).TotalSeconds -lt 15 } catch { }
    $sessionReports += [pscustomobject]@{
        Session = $session.sessionId
        WindowTitle = if ($session.psobject.Properties['windowTitle']) { $session.windowTitle } else { 'Guildhall' }
        Branch = if ($session.psobject.Properties['branch']) { $session.branch } else { Split-Path -Leaf $checkoutRoot }
        Version = if ($session.psobject.Properties['version']) { $session.version } else { 'v0.1.0' }
        Mode = $session.mode
        Checkout = $checkoutRoot
        Ports = if ($session.frontendPort) { "host=$($session.hostPort), frontend=$($session.frontendPort)" } else { "host=$($session.hostPort)" }
        StartedUtc = $session.startedUtc
        Host = if ($statusIsFresh) { $session.hostStatus } else { 'stale status snapshot' }
        SQL = if ($statusIsFresh) { $session.sqlStatus } else { 'stale status snapshot' }
        StatusCheckedUtc = $session.statusCheckedUtc
        WorkingTree = $gitState
        SourceFingerprintAtStart = $session.sourceFingerprintAtStart
        CurrentSourceFingerprint = if ([IO.Path]::GetFullPath($checkoutRoot) -eq [IO.Path]::GetFullPath($RepoRoot)) { $currentFingerprint } else { 'other worktree' }
        DesktopBinary = $binaryIdentity
        Processes = @($entry.Processes | Select-Object Role, PID, State)
    }
}

$listenerSummary = @(Get-VerifiedGuildhallPortListeners -Ports ($portsToInspect.ToArray()) | Select-Object Port, PID, Process, Ownership)

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
        WindowTitle = $report.WindowTitle
        Branch = $report.Branch
        Version = $report.Version
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
