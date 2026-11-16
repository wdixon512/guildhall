#Requires -Version 7.0
[CmdletBinding()]
param(
    [switch]$HostOnly,
    [switch]$BuiltDesktop,
    [switch]$UseGpu,
    [ValidateRange(0,900)][int]$KeepModelWarmSeconds = 0,
    [ValidateRange(1,120)][int]$RunForSeconds = 0,
    [ValidateRange(1024,65535)][int]$HostPort = 43120,
    [ValidateRange(1024,65535)][int]$FrontendPort = 1420,
    [switch]$AutoPort,
    [switch]$Replace,
    [string]$WindowTag = '',
    [switch]$SkipMigrations,
    [switch]$ApproveMigrations
)
. "$PSScriptRoot/../misc/Common.ps1"
if ($HostOnly -and $BuiltDesktop) { throw 'Choose HostOnly or BuiltDesktop, not both.' }
$targetPorts = if (-not $HostOnly -and -not $BuiltDesktop) { @($HostPort, $FrontendPort) } else { @($HostPort) }
if ($Replace) {
    $conflicting = Get-RegisteredDevSessions | Where-Object {
        [IO.Path]::GetFullPath($_.Session.checkout) -eq [IO.Path]::GetFullPath($RepoRoot) -or
        ($targetPorts -contains [int]$_.Session.hostPort) -or
        ($null -ne $_.Session.frontendPort -and ($targetPorts -contains [int]$_.Session.frontendPort))
    }
    foreach ($entry in $conflicting) {
        Write-Output "Stopping existing Guildhall session $($entry.SessionId) ($($entry.Session.checkout))..."
        Stop-RegisteredDevSession $entry
    }
    Stop-VerifiedGuildhallPortListeners -Ports $targetPorts
}
if ($AutoPort) {
    $HostPort = Get-FreeLocalPort -PreferredPort $HostPort
    if (-not $HostOnly -and -not $BuiltDesktop) {
        $FrontendPort = Get-FreeLocalPort -PreferredPort $FrontendPort -ExcludePorts @($HostPort)
    }
}
Assert-LocalPortFree $HostPort
if (-not $HostOnly -and -not $BuiltDesktop) { Assert-LocalPortFree $FrontendPort }
$hostUrl = "http://127.0.0.1:$HostPort"
$frontendUrl = "http://127.0.0.1:$FrontendPort"
$windowMeta = Get-InstanceWindowMetadata -HostPort $HostPort -FrontendPort $(if (-not $HostOnly -and -not $BuiltDesktop) { $FrontendPort } else { 0 }) -WindowTag $WindowTag
$dll = Join-Path $RepoRoot 'services/agent-host/bin/Debug/net10.0/Guildhall.AgentHost.dll'
if ($BuiltDesktop -and -not (Test-Path -LiteralPath $dll)) { throw 'Build the solution before starting development.' }
$token = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$shared = New-ChildEnvironment
$shared['GUILDHALL_HOST_URL'] = $hostUrl
$shared['GUILDHALL_FRONTEND_URL'] = $frontendUrl
$shared['GUILDHALL_SESSION_TOKEN'] = $token
$shared['GUILDHALL_WINDOW_TITLE'] = $windowMeta.WindowTitle
$shared['GUILDHALL_INSTANCE_TAG'] = $windowMeta.InstanceTag
$hostEnvironment = $shared.Clone()
$hostEnvironment['GUILDHALL_USE_GPU'] = if ($UseGpu) { 'true' } else { 'false' }
$hostEnvironment['GUILDHALL_MODEL_KEEP_ALIVE_SECONDS'] = [string]$KeepModelWarmSeconds
Write-Output "Window: $($windowMeta.WindowTitle)"
Write-Output "Inference mode: $(if ($UseGpu) { 'GPU automatic offload' } else { 'CPU only, 4 threads' }); model retention after replies: $KeepModelWarmSeconds seconds; host: $hostUrl$(if (-not $HostOnly -and -not $BuiltDesktop) { "; frontend: $frontendUrl" })."
$hostEnvironment['GUILDHALL_OLLAMA_URL'] = if ($env:GUILDHALL_OLLAMA_URL) { $env:GUILDHALL_OLLAMA_URL } else { 'http://127.0.0.1:11434' }
$runtimeConnection = Get-LocalSqlConnection -Runtime
if ($runtimeConnection) {
    $hostEnvironment['ConnectionStrings__Guildhall'] = $runtimeConnection
    if (-not $SkipMigrations) {
        Invoke-AutoDatabaseMigration -DatabaseName 'GuildhallDev' -Approve:$ApproveMigrations
    }
}
else { Write-Warning 'Database is not configured; the desktop will show this state.' }
$hostProcess = $null
$desktopProcess = $null
$viteProcess = $null
$sessionId = [guid]::NewGuid().ToString('N')
$sessionDirectory = Join-Path $LocalDirectory "dev-sessions/$sessionId"
$sessionManifestPath = Join-Path $sessionDirectory 'session.json'
$sharedManifestPath = Join-Path $SharedSessionsDirectory "$sessionId.json"
New-Item -ItemType Directory -Path $sessionDirectory -Force | Out-Null
try { New-Item -ItemType Directory -Path $SharedSessionsDirectory -Force -ErrorAction Stop | Out-Null } catch { }
try {
    $hostArguments = if ($BuiltDesktop) { @($dll) } else { @('watch', '--project', (Join-Path $RepoRoot 'services/agent-host/Guildhall.AgentHost.csproj'), 'run', '--no-launch-profile') }
    $hostProcess = Start-OwnedChild (Get-Command dotnet).Source $hostArguments $hostEnvironment $RepoRoot
    $deadline = [datetime]::UtcNow.AddSeconds(15)
    $healthy = $false
    while ([datetime]::UtcNow -lt $deadline) {
        if ($hostProcess.HasExited) { throw 'Agent host exited during startup; inspect its sanitized error above.' }
        try {
            $health = Invoke-RestMethod -Uri "$hostUrl/health" -Headers @{ Authorization = "Bearer $token" } -TimeoutSec 1
            if ($health.status -eq 'ok') { $healthy = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 150
    }
    if (-not $healthy) { throw 'Agent host did not become healthy within 15 seconds.' }
    Write-Output 'Agent host is ready on loopback. SQL and Ollama status are reported separately.'
    if (-not $HostOnly) {
        if ($BuiltDesktop) {
            $desktopExecutable = Join-Path $RepoRoot 'apps/desktop/src-tauri/target/debug/guildhall-desktop.exe'
            if (-not (Test-Path -LiteralPath $desktopExecutable)) { throw 'Build the embedded desktop with: npm.cmd --prefix apps/desktop run tauri -- build --debug --no-bundle' }
            $desktopProcess = Start-OwnedChild $desktopExecutable @() $shared
        } else {
        # Own Vite directly: a Tauri beforeDevCommand can outlive its parent on Windows.
        # Vite receives build environment only, without the session token or SQL credentials.
        $viteCli = Join-Path $RepoRoot 'apps/desktop/node_modules/vite/bin/vite.js'
        $viteProcess = Start-OwnedChild (Get-Command node).Source @($viteCli,'--host','127.0.0.1','--port',[string]$FrontendPort,'--strictPort') (New-ChildEnvironment) (Join-Path $RepoRoot 'apps/desktop')
        $viteDeadline = [datetime]::UtcNow.AddSeconds(15)
        $viteReady = $false
        while ([datetime]::UtcNow -lt $viteDeadline) {
            if ($viteProcess.HasExited) { throw 'Frontend dev server exited during startup.' }
            try { $null = Invoke-WebRequest -Uri $frontendUrl -TimeoutSec 1; $viteReady = $true; break } catch { }
            Start-Sleep -Milliseconds 150
        }
        if (-not $viteReady) { throw 'Frontend dev server did not become ready within 15 seconds.' }
        $builtDevBinary = [IO.Path]::GetFullPath((Join-Path $RepoRoot 'apps/desktop/src-tauri/target/debug/guildhall-desktop.exe'))
        $binaryInUse = (Test-Path -LiteralPath $builtDevBinary) -and (@(Get-Process -Name 'guildhall-desktop' -ErrorAction SilentlyContinue | Where-Object {
            try { [IO.Path]::GetFullPath($_.Path) -eq $builtDevBinary } catch { $false }
        }).Count -gt 0)
        if ($binaryInUse) {
            # Reuse the already-built dev binary from a per-session copy so Cargo does not fail trying to relink a locked .exe.
            $sessionDesktopExecutable = Join-Path $sessionDirectory 'guildhall-desktop.exe'
            Copy-Item -LiteralPath $builtDevBinary -Destination $sessionDesktopExecutable -Force
            $desktopProcess = Start-OwnedChild $sessionDesktopExecutable @() $shared
        } else {
        # Launch the JS CLI directly, without shell expansion or secret-bearing arguments.
        $cli = Join-Path $RepoRoot 'apps/desktop/node_modules/@tauri-apps/cli/tauri.js'
        $tauriOverride = @{
            build = @{ beforeDevCommand = $null; devUrl = $frontendUrl }
            app = @{
                windows = @(
                    @{
                        label = 'main'
                        create = $false
                        title = $windowMeta.WindowTitle
                        width = 1440
                        height = 940
                        minWidth = 420
                        minHeight = 600
                    }
                )
                security = @{
                    csp = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src ipc: http://ipc.localhost $hostUrl http://127.0.0.1:*; img-src 'self' data: blob:; object-src 'none'; base-uri 'none'; frame-src 'none'"
                    devCsp = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; connect-src ipc: http://ipc.localhost $hostUrl http://127.0.0.1:* ws://127.0.0.1:$FrontendPort ws://127.0.0.1:*; img-src 'self' data: blob:; object-src 'none'; base-uri 'none'; frame-src 'none'"
                }
            }
        } | ConvertTo-Json -Depth 6 -Compress
        $desktopProcess = Start-OwnedChild (Get-Command node).Source @($cli,'dev','--config',$tauriOverride) $shared (Join-Path $RepoRoot 'apps/desktop')
        }
        }
    }
    $binaryPath = Join-Path $RepoRoot 'apps/desktop/src-tauri/target/debug/guildhall-desktop.exe'
    $manifest = [ordered]@{
        sessionId = $sessionId
        checkout = $RepoRoot
        branch = $windowMeta.Branch
        version = $windowMeta.Version
        windowTitle = $windowMeta.WindowTitle
        instanceTag = $windowMeta.InstanceTag
        mode = if ($BuiltDesktop) { 'BuiltDesktop' } elseif ($HostOnly) { 'HostOnly' } else { 'Development' }
        startedUtc = [datetime]::UtcNow.ToString('O')
        launcherPid = $PID
        hostLauncherPid = $hostProcess.Id
        frontendPid = if ($viteProcess) { $viteProcess.Id } else { $null }
        desktopPid = if ($desktopProcess) { $desktopProcess.Id } else { $null }
        hostPort = $HostPort
        frontendPort = if ($HostOnly -or $BuiltDesktop) { $null } else { $FrontendPort }
        sourceFingerprintAtStart = Get-SourceFingerprint
        desktopBinarySha256AtStart = if ($BuiltDesktop -and (Test-Path -LiteralPath $binaryPath)) { (Get-FileHash -LiteralPath $binaryPath -Algorithm SHA256).Hash } else { $null }
        hostStatus = 'ready'
        sqlStatus = 'checking'
        statusCheckedUtc = [datetime]::UtcNow.ToString('O')
    }
    $writeManifest = {
        $json = $manifest | ConvertTo-Json -Depth 3
        [IO.File]::WriteAllText($sessionManifestPath, $json, [Text.UTF8Encoding]::new($false))
        try { [IO.File]::WriteAllText($sharedManifestPath, $json, [Text.UTF8Encoding]::new($false)) } catch { }
    }
    & $writeManifest
    Write-Output "Development session: $sessionId (status: pwsh -NoProfile -File .\scripts\startup\Get-DevStatus.ps1)"
    $stopAt = if ($RunForSeconds -gt 0) { [datetime]::UtcNow.AddSeconds($RunForSeconds) } else { [datetime]::MaxValue }
    $nextStatusCheck = [datetime]::MinValue
    while ([datetime]::UtcNow -lt $stopAt) {
        if ($hostProcess.HasExited) { throw 'Agent host stopped unexpectedly.' }
        if ($viteProcess -and $viteProcess.HasExited) { throw 'Frontend dev server stopped unexpectedly.' }
        if ($desktopProcess -and $desktopProcess.HasExited) {
            if ($desktopProcess.ExitCode -ne 0) { throw "Desktop exited with code $($desktopProcess.ExitCode)." }
            break
        }
        if ([datetime]::UtcNow -ge $nextStatusCheck) {
            try {
                $statusHeaders = @{ Authorization = "Bearer $token" }
                $health = Invoke-RestMethod -Uri "$hostUrl/health" -Headers $statusHeaders -TimeoutSec 2
                $manifest.hostStatus = if ($health.status -eq 'ok' -and $health.apiVersion -eq 1) { 'ready' } else { 'invalid response' }
                try {
                    $readiness = Invoke-RestMethod -Uri "$hostUrl/ready" -Headers $statusHeaders -TimeoutSec 3
                    $manifest.sqlStatus = $readiness.status
                } catch {
                    $reason = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
                    $manifest.sqlStatus = if ($reason.status) { "$($reason.status):$($reason.reason)" } else { 'unavailable' }
                }
            } catch { $manifest.hostStatus = 'unavailable' }
            finally { $statusHeaders = $null }
            $manifest.statusCheckedUtc = [datetime]::UtcNow.ToString('O')
            & $writeManifest
            $nextStatusCheck = [datetime]::UtcNow.AddSeconds(5)
        }
        Start-Sleep -Milliseconds 200
    }
} finally {
    Stop-OwnedChild $desktopProcess
    Stop-OwnedChild $viteProcess
    Stop-OwnedChild $hostProcess
    Remove-Item -LiteralPath $sessionManifestPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $sharedManifestPath -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $sessionDirectory) { Remove-Item -LiteralPath $sessionDirectory -Recurse -Force -ErrorAction SilentlyContinue }
    $token = $null; $runtimeConnection = $null
    Write-Output 'Owned development processes stopped.'
}
