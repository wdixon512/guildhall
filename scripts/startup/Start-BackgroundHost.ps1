#Requires -Version 7.0
[CmdletBinding()]
param(
    [switch]$UseGpu,
    [ValidateRange(0,900)][int]$KeepModelWarmSeconds = 900,
    [ValidateRange(512,131072)][int]$MaxTurnContextTokens = 32768,
    [ValidateRange(1024,524288)][int]$MaxSessionTokens = 131072,
    [ValidateRange(1024,65535)][int]$HostPort = 43120,
    [switch]$SkipMigrations,
    [switch]$ApproveMigrations,
    [switch]$Status,
    [switch]$Stop,
    [switch]$RegisterLogonTask,
    [switch]$UnregisterLogonTask
)

. "$PSScriptRoot/../misc/Common.ps1"

$bgDir = Join-Path $env:LOCALAPPDATA 'Guildhall\dev'
$bgManifestPath = Join-Path $bgDir 'background-host.json'
$taskName = 'GuildhallAgentHostBackground'

if ($UnregisterLogonTask) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Output "Unregistered scheduled logon task '$taskName' (if present)."
    return
}

if ($RegisterLogonTask) {
    $scriptPath = $PSCommandPath
    $argList = "-NoProfile -WindowStyle Hidden -File `"$scriptPath`" -UseGpu:`$$($UseGpu.IsPresent) -KeepModelWarmSeconds $KeepModelWarmSeconds -HostPort $HostPort -SkipMigrations"
    $action = New-ScheduledTaskAction -Execute 'pwsh.exe' -Argument $argList -WorkingDirectory $RepoRoot
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Description 'Runs Guildhall.AgentHost in the background for local scheduled automations.' -Force | Out-Null
    Write-Output "Registered Windows Task Scheduler logon task '$taskName'."
    return
}

function Read-BackgroundManifest {
    if (-not (Test-Path -LiteralPath $bgManifestPath)) { return $null }
    try { return Get-Content -LiteralPath $bgManifestPath -Raw | ConvertFrom-Json }
    catch { return $null }
}

function Unprotect-BgToken([string]$encryptedHex) {
    if ([string]::IsNullOrWhiteSpace($encryptedHex)) { return $null }
    $bytes = [Convert]::FromHexString($encryptedHex)
    $plain = [System.Security.Cryptography.ProtectedData]::Unprotect($bytes, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
    return [System.Text.Encoding]::UTF8.GetString($plain)
}

function Protect-BgToken([string]$plainToken) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($plainToken)
    $protected = [System.Security.Cryptography.ProtectedData]::Protect($bytes, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
    return [Convert]::ToHexString($protected)
}

if ($Status) {
    $m = Read-BackgroundManifest
    if (-not $m) { Write-Output 'Background host: not running (no manifest).'; return }
    $proc = Get-Process -Id ([int]$m.pid) -ErrorAction SilentlyContinue
    if (-not $proc) { Write-Output "Background host: stale manifest (PID $($m.pid) exited)."; return }
    try {
        $tok = Unprotect-BgToken $m.protectedToken
        $health = Invoke-RestMethod -Uri "$($m.hostUrl)/health" -Headers @{ Authorization = "Bearer $tok" } -TimeoutSec 2
        Write-Output "Background host: running (PID $($m.pid), $($m.hostUrl), status=$($health.status))."
    } catch {
        Write-Output "Background host: process $($m.pid) alive, health probe failed."
    }
    return
}

if ($Stop) {
    $m = Read-BackgroundManifest
    if ($m -and $m.pid) {
        Stop-Process -Id ([int]$m.pid) -Force -ErrorAction SilentlyContinue
        Write-Output "Stopped background host PID $($m.pid)."
    }
    Remove-Item -LiteralPath $bgManifestPath -Force -ErrorAction SilentlyContinue
    return
}

$existing = Read-BackgroundManifest
if ($existing -and $existing.pid) {
    $proc = Get-Process -Id ([int]$existing.pid) -ErrorAction SilentlyContinue
    if ($proc) {
        Write-Output "Background host already running (PID $($existing.pid), $($existing.hostUrl))."
        return
    }
}

Assert-LocalPortFree $HostPort
$hostUrl = "http://127.0.0.1:$HostPort"
$token = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

$runtimeConnection = Get-LocalSqlConnection -Runtime
if ($runtimeConnection -and -not $SkipMigrations) {
    Invoke-AutoDatabaseMigration -DatabaseName 'GuildhallDev' -Approve:$ApproveMigrations
}

$dll = Join-Path $RepoRoot 'services/agent-host/bin/Release/net10.0/Guildhall.AgentHost.dll'
if (-not (Test-Path -LiteralPath $dll)) {
    $dll = Join-Path $RepoRoot 'services/agent-host/bin/Debug/net10.0/Guildhall.AgentHost.dll'
}

$envMap = New-ChildEnvironment
$envMap['GUILDHALL_HOST_URL'] = $hostUrl
$envMap['GUILDHALL_SESSION_TOKEN'] = $token
$envMap['GUILDHALL_USE_GPU'] = if ($UseGpu) { 'true' } else { 'false' }
$envMap['GUILDHALL_MODEL_KEEP_ALIVE_SECONDS'] = [string]$KeepModelWarmSeconds
$envMap['CrossTurnBudget__MaxTurnContextTokens'] = [string]$MaxTurnContextTokens
$envMap['CrossTurnBudget__MaxSessionTokens'] = [string]$MaxSessionTokens
$envMap['GUILDHALL_OLLAMA_URL'] = if ($env:GUILDHALL_OLLAMA_URL) { $env:GUILDHALL_OLLAMA_URL } else { 'http://127.0.0.1:11434' }
if ($runtimeConnection) { $envMap['ConnectionStrings__Guildhall'] = $runtimeConnection }

$args = if (Test-Path -LiteralPath $dll) { @($dll) } else { @('run', '--project', (Join-Path $RepoRoot 'services/agent-host/Guildhall.AgentHost.csproj'), '--no-launch-profile') }
$proc = Start-OwnedChild (Get-Command dotnet).Source $args $envMap $RepoRoot

$deadline = [datetime]::UtcNow.AddSeconds(15)
$healthy = $false
while ([datetime]::UtcNow -lt $deadline) {
    if ($proc.HasExited) { throw 'Background agent host exited during startup.' }
    try {
        $health = Invoke-RestMethod -Uri "$hostUrl/health" -Headers @{ Authorization = "Bearer $token" } -TimeoutSec 1
        if ($health.status -eq 'ok') { $healthy = $true; break }
    } catch { }
    Start-Sleep -Milliseconds 150
}
if (-not $healthy) {
    Stop-OwnedChild $proc
    throw 'Background agent host failed health check within 15 seconds.'
}

New-Item -ItemType Directory -Path $bgDir -Force | Out-Null
[ordered]@{
    pid = $proc.Id
    hostUrl = $hostUrl
    hostPort = $HostPort
    checkout = $RepoRoot
    startedUtc = [datetime]::UtcNow.ToString('O')
    protectedToken = Protect-BgToken $token
} | ConvertTo-Json | Set-Content -LiteralPath $bgManifestPath -Encoding UTF8

$token = $null
$runtimeConnection = $null
Write-Output "Detached background host started (PID $($proc.Id), $hostUrl). Scheduled automations will continue running after desktop windows close."
