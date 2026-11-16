#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$SessionId,
    [string]$Checkout,
    [ValidateRange(0,65535)][int]$Port = 0,
    [switch]$All
)
. "$PSScriptRoot/../misc/Common.ps1"

$sessions = Get-RegisteredDevSessions
$targets = if ($All) {
    $sessions
} elseif ($SessionId) {
    $sessions | Where-Object { $_.SessionId -eq $SessionId }
} elseif ($Checkout) {
    $fullCheckout = [IO.Path]::GetFullPath($Checkout)
    $sessions | Where-Object { [IO.Path]::GetFullPath($_.Session.checkout) -eq $fullCheckout }
} elseif ($Port -gt 0) {
    $sessions | Where-Object { [int]$_.Session.hostPort -eq $Port -or ($null -ne $_.Session.frontendPort -and [int]$_.Session.frontendPort -eq $Port) }
} else {
    $fullCurrent = [IO.Path]::GetFullPath($RepoRoot)
    $sessions | Where-Object { [IO.Path]::GetFullPath($_.Session.checkout) -eq $fullCurrent }
}
$targets = @($targets)

foreach ($target in $targets) {
    Write-Output "Stopping Guildhall session $($target.SessionId) ($($target.Session.checkout)) [host=$($target.Session.hostPort), frontend=$($target.Session.frontendPort)]..."
    Stop-RegisteredDevSession $target
}

$portsToClean = if ($Port -gt 0) {
    @($Port)
} elseif ($All -or -not ($SessionId -or $Checkout)) {
    @(43120, 1420)
} else {
    @()
}

if ($portsToClean.Count -gt 0) {
    $unregistered = @(Get-VerifiedGuildhallPortListeners -Ports $portsToClean | Where-Object { $_.IsGuildhall -and -not $_.SessionId })
    if ($All -or ($Port -gt 0) -or ($unregistered | Where-Object { $_.Checkout -and [IO.Path]::GetFullPath($_.Checkout) -eq [IO.Path]::GetFullPath($RepoRoot) })) {
        foreach ($listener in $unregistered) {
            if ($All -or ($Port -gt 0) -or ($listener.Checkout -and [IO.Path]::GetFullPath($listener.Checkout) -eq [IO.Path]::GetFullPath($RepoRoot))) {
                Write-Output "Stopping unregistered Guildhall listener $($listener.Process) (PID $($listener.PID)) on port $($listener.Port)..."
                Stop-VerifiedGuildhallPortListeners -Ports @($listener.Port)
            }
        }
    }
}

if (-not $targets.Count) {
    $otherSessions = @($sessions | Where-Object { [IO.Path]::GetFullPath($_.Session.checkout) -ne [IO.Path]::GetFullPath($RepoRoot) })
    $otherListeners = @(Get-VerifiedGuildhallPortListeners -Ports @(43120, 1420) | Where-Object { $_.IsGuildhall })
    if ($otherSessions.Count -gt 0 -or $otherListeners.Count -gt 0) {
        Write-Output 'No session matched the current checkout. Other Guildhall instances are active — pass -All to stop all instances:'
        foreach ($other in $otherSessions) {
            Write-Output "  Session $($other.SessionId) in '$($other.Session.checkout)' (host=$($other.Session.hostPort), frontend=$($other.Session.frontendPort))"
        }
        foreach ($listener in $otherListeners) {
            Write-Output "  Port $($listener.Port): $($listener.Process) (PID $($listener.PID)) [$($listener.Ownership)]"
        }
    } else {
        Write-Output 'No active Guildhall dev sessions or listeners found.'
    }
}
