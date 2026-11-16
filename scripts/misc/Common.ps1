#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LocalDirectory = Join-Path $RepoRoot '.local'
$LegacyCredentialPath = Join-Path $LocalDirectory 'runtime.credential.xml'
$SharedCredentialDirectory = if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Guildhall/dev' } else { Join-Path $HOME '.local/share/Guildhall/dev' }
$SharedCredentialPath = Join-Path $SharedCredentialDirectory 'runtime.credential.xml'
$SharedSessionsDirectory = Join-Path $SharedCredentialDirectory 'sessions'
function Test-SafePath {
    param([string]$PathToTest)
    try { return Test-Path -LiteralPath $PathToTest -ErrorAction Stop } catch { return $false }
}
# Prefer one user-local credential across worktrees, while preserving existing checkout-local setups.
$CredentialPath = if (Test-SafePath $SharedCredentialPath) { $SharedCredentialPath } elseif (Test-SafePath $LegacyCredentialPath) { $LegacyCredentialPath } else { $SharedCredentialPath }

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable failed (exit $LASTEXITCODE)." }
}

function Get-LocalSqlConnection {
    param([string]$Database = 'GuildhallDev', [switch]$Runtime)
    if ($Database -ne 'GuildhallDev' -and $Database -notmatch '^GuildhallTest_[a-f0-9]{32}$') {
        throw 'Only dedicated Guildhall database names are allowed.'
    }
    $builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new("Server=localhost;Database=$Database;Encrypt=true;TrustServerCertificate=true;Connect Timeout=3")
    if ($Runtime) {
        if (-not (Test-SafePath $CredentialPath)) { return $null }
        $credential = Import-Clixml -LiteralPath $CredentialPath
        if ($credential -isnot [pscredential] -or $credential.UserName -ne 'guildhall_runtime') { throw 'Invalid saved runtime credential.' }
        $builder['User ID'] = $credential.UserName
        $builder['Password'] = $credential.GetNetworkCredential().Password
    } else { $builder['Integrated Security'] = $true }
    return $builder.ConnectionString
}

function Invoke-AutoDatabaseMigration {
    param([string]$DatabaseName = 'GuildhallDev', [switch]$Approve)
    $migrationsDir = Join-Path $RepoRoot 'services/agent-host/Persistence/Migrations'
    if (-not (Test-SafePath $migrationsDir)) { return }
    $migrations = @(Get-ChildItem -LiteralPath $migrationsDir -File |
        Where-Object Name -Match '^\d{14}_[A-Za-z0-9_]+\.cs$' | Sort-Object Name | ForEach-Object BaseName)
    if (-not $migrations.Count) { return }
    $applied = @()
    try {
        $connection = Get-LocalSqlConnection -Database $DatabaseName
        $db = [System.Data.SqlClient.SqlConnection]::new($connection)
        try {
            $db.Open()
            $command = $db.CreateCommand()
            $command.CommandText = "IF OBJECT_ID(N'dbo.__EFMigrationsHistory') IS NOT NULL SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;"
            $reader = $command.ExecuteReader()
            while ($reader.Read()) { $applied += $reader.GetString(0) }
            $reader.Close()
        } finally { $db.Dispose() }
    } catch {
        Write-Warning "Auto-migration check skipped (SQL database '$DatabaseName' is unreachable)."
        return
    }
    $pending = @($migrations | Where-Object { $applied -notcontains $_ })
    if (-not $pending.Count) { return }
    $requiresApproval = @()
    foreach ($migrationId in $pending) {
        $filePath = Join-Path $migrationsDir "$migrationId.cs"
        if (Test-SafePath $filePath) {
            $content = Get-Content -LiteralPath $filePath -Raw
            $upSection = if ($content -match '(?s)protected override void Up\(.*?\)(.*)protected override void Down\(') { $Matches[1] } else { $content }
            if ($upSection -match '\b(DropTable|DropColumn|DROP\s+|TRUNCATE\s+|GRANT\s+|REVOKE\s+|DENY\s+)') {
                $requiresApproval += $migrationId
            }
        }
    }
    if ($requiresApproval.Count -gt 0 -and -not $Approve) {
        Write-Warning "Pending migration(s) contain destructive or permission-changing SQL ($($requiresApproval -join ', ')). Run 'pwsh -NoProfile -File .\scripts\database\Migrate-Database.ps1 -DatabaseName $DatabaseName' to preview, or pass -ApproveMigrations to Start-Dev.ps1."
        return
    }
    Write-Output "Applying $($pending.Count) pending database migration(s) to $DatabaseName ($($pending -join ', '))..."
    & (Join-Path $RepoRoot 'scripts/database/Migrate-Database.ps1') -DatabaseName $DatabaseName -Execute
}

function Test-LocalPortFree {
    param([int]$Port)
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
    try { $listener.Start(); return $true }
    catch { return $false }
    finally { try { $listener.Stop() } catch { } }
}

function Get-FreeLocalPort {
    param([int]$PreferredPort, [int[]]$ExcludePorts = @())
    for ($candidate = $PreferredPort; $candidate -lt ($PreferredPort + 100); $candidate++) {
        if ($ExcludePorts -notcontains $candidate -and (Test-LocalPortFree $candidate)) { return $candidate }
    }
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    } finally {
        try { $listener.Stop() } catch { }
    }
}

function Get-InstanceWindowMetadata {
    param([int]$HostPort = 43120, [int]$FrontendPort = 0, [string]$WindowTag = '')
    $version = '0.1.0'
    $tauriConfPath = Join-Path $RepoRoot 'apps/desktop/src-tauri/tauri.conf.json'
    if (Test-SafePath $tauriConfPath) {
        try {
            $tauriConf = Get-Content -LiteralPath $tauriConfPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            if ($tauriConf.version) { $version = [string]$tauriConf.version }
        } catch { }
    }
    $versionTag = if ($version.StartsWith('v', [StringComparison]::OrdinalIgnoreCase)) { $version } else { "v$version" }
    $branch = ''
    try {
        $gitBranch = (& git -C $RepoRoot rev-parse --abbrev-ref HEAD 2>$null | Select-Object -First 1)
        if ($LASTEXITCODE -eq 0 -and $gitBranch) { $branch = ([string]$gitBranch).Trim() }
    } catch { }
    $worktreeLeaf = Split-Path -Leaf $RepoRoot
    if (-not $branch -or $branch -eq 'HEAD') {
        $branch = $worktreeLeaf
    } elseif ($worktreeLeaf -and $worktreeLeaf -ne 'guildhall' -and $branch -notlike "*$worktreeLeaf*") {
        $branch = "$branch ($worktreeLeaf)"
    }
    $cleanTag = if ($WindowTag) { $WindowTag.Trim() } else { '' }
    $labelCore = if ($cleanTag) { "$branch · $cleanTag" } else { $branch }
    $portSuffix = if ($FrontendPort -gt 0) { ":$HostPort/:$FrontendPort" } else { ":$HostPort" }
    $instanceTag = "$versionTag · $labelCore $portSuffix"
    $windowTitle = "Guildhall $versionTag [$labelCore | $portSuffix]"
    return [pscustomobject]@{
        Version = $versionTag
        Branch = $branch
        WindowTag = $cleanTag
        InstanceTag = $instanceTag
        WindowTitle = $windowTitle
    }
}

function Get-RegisteredDevSessions {
    param([switch]$IncludeExited)
    $manifestFiles = [System.Collections.Generic.List[string]]::new()
    if (Test-SafePath $SharedSessionsDirectory) {
        try {
            foreach ($file in (Get-ChildItem -LiteralPath $SharedSessionsDirectory -Filter '*.json' -File -ErrorAction Stop)) {
                $manifestFiles.Add($file.FullName)
            }
        } catch { }
    }
    $localSessionRoot = Join-Path $LocalDirectory 'dev-sessions'
    if (Test-SafePath $localSessionRoot) {
        try {
            foreach ($dir in (Get-ChildItem -LiteralPath $localSessionRoot -Directory -ErrorAction Stop)) {
                $candidate = Join-Path $dir.FullName 'session.json'
                if (Test-SafePath $candidate) { $manifestFiles.Add($candidate) }
            }
        } catch { }
    }
    $byId = @{}
    foreach ($file in $manifestFiles) {
        try {
            $session = Get-Content -LiteralPath $file -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            if (-not $session.sessionId) { continue }
            if (-not $byId.ContainsKey($session.sessionId)) {
                $byId[$session.sessionId] = [ordered]@{ Session = $session; Paths = [System.Collections.Generic.List[string]]::new() }
            }
            if (-not $byId[$session.sessionId].Paths.Contains($file)) {
                $byId[$session.sessionId].Paths.Add($file)
            }
        } catch { }
    }
    $results = @()
    foreach ($entry in $byId.Values) {
        $session = $entry.Session
        $processes = foreach ($key in @('launcherPid','hostLauncherPid','frontendPid','desktopPid')) {
            $processId = $session.$key
            if ($null -eq $processId) { continue }
            $process = Get-Process -Id ([int]$processId) -ErrorAction SilentlyContinue
            [pscustomobject]@{
                Role = $key
                PID = [int]$processId
                ProcessName = if ($process) { $process.ProcessName } else { $null }
                State = if ($process) { 'running' } else { 'exited' }
            }
        }
        $processes = @($processes)
        $anyRunning = @($processes | Where-Object { $_.State -eq 'running' }).Count -gt 0
        if (-not $anyRunning -and -not $IncludeExited) {
            foreach ($path in $entry.Paths) {
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
                $parent = Split-Path -Parent $path
                if ((Split-Path -Leaf $parent) -eq $session.sessionId) {
                    Remove-Item -LiteralPath $parent -Force -ErrorAction SilentlyContinue
                }
            }
            continue
        }
        $results += [pscustomobject]@{
            SessionId = [string]$session.sessionId
            Session = $session
            ManifestPaths = @($entry.Paths)
            Processes = $processes
            AnyRunning = $anyRunning
        }
    }
    return @($results)
}

function Stop-RegisteredDevSession {
    param([Parameter(Mandatory)]$SessionEntry)
    $allowedNames = @('guildhall-desktop','Guildhall.AgentHost','dotnet','node','cargo','pwsh','powershell')
    $orderedRoles = @('desktopPid','frontendPid','hostLauncherPid','launcherPid')
    foreach ($role in $orderedRoles) {
        $procInfo = $SessionEntry.Processes | Where-Object { $_.Role -eq $role } | Select-Object -First 1
        if (-not $procInfo -or $procInfo.PID -eq $PID) { continue }
        $live = Get-Process -Id $procInfo.PID -ErrorAction SilentlyContinue
        if ($live -and ($allowedNames -contains $live.ProcessName)) {
            try { $live.Kill($true); $live.WaitForExit(3000) | Out-Null } catch { }
        }
    }
    foreach ($path in $SessionEntry.ManifestPaths) {
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        $parent = Split-Path -Parent $path
        if ((Split-Path -Leaf $parent) -eq $SessionEntry.SessionId) {
            Remove-Item -LiteralPath $parent -Force -ErrorAction SilentlyContinue
        }
    }
    $portsToWait = @($SessionEntry.Session.hostPort, $SessionEntry.Session.frontendPort) | Where-Object { $null -ne $_ }
    $deadline = [datetime]::UtcNow.AddSeconds(3)
    while ([datetime]::UtcNow -lt $deadline) {
        $allFree = $true
        foreach ($p in $portsToWait) {
            if (-not (Test-LocalPortFree ([int]$p))) { $allFree = $false; break }
        }
        if ($allFree) { break }
        Start-Sleep -Milliseconds 100
    }
}

function Get-VerifiedGuildhallPortListeners {
    param([int[]]$Ports = @(43120, 1420))
    $registeredSessions = Get-RegisteredDevSessions
    $results = @()
    foreach ($port in ($Ports | Select-Object -Unique)) {
        $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)
        foreach ($listener in $listeners) {
            $ownerPid = [int]$listener.OwningProcess
            $process = Get-Process -Id $ownerPid -ErrorAction SilentlyContinue
            $procName = if ($process) { $process.ProcessName } else { 'exited' }
            $path = if ($process) { try { $process.Path } catch { $null } } else { $null }
            $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId=$ownerPid" -ErrorAction SilentlyContinue
            $cmdLine = if ($processInfo) { [string]$processInfo.CommandLine } else { '' }
            $matchedSession = $registeredSessions | Where-Object {
                ($_.Processes | Where-Object { $_.PID -eq $ownerPid }) -or
                $_.Session.hostPort -eq $port -or $_.Session.frontendPort -eq $port
            } | Select-Object -First 1
            $isCurrentCheckout = ($path -and $path.StartsWith(($RepoRoot.TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase)) -or
                ($cmdLine -match [regex]::Escape((Join-Path $RepoRoot 'apps/desktop/node_modules/vite/bin/vite.js'))) -or
                ($cmdLine -match [regex]::Escape((Join-Path $RepoRoot 'services/agent-host')))
            $isAnyGuildhall = $null -ne $matchedSession -or $isCurrentCheckout -or
                $procName -eq 'Guildhall.AgentHost' -or $procName -eq 'guildhall-desktop' -or
                ($cmdLine -match 'Guildhall\.AgentHost') -or
                ($cmdLine -match 'apps[\\/]desktop[\\/]node_modules[\\/]vite[\\/]bin[\\/]vite\.js')
            $checkoutLabel = if ($matchedSession) { $matchedSession.Session.checkout } elseif ($isCurrentCheckout) { $RepoRoot } else { $null }
            $ownership = if ($matchedSession) {
                "Guildhall session $($matchedSession.SessionId) ($($matchedSession.Session.checkout))"
            } elseif ($isCurrentCheckout) {
                'Guildhall checkout'
            } elseif ($isAnyGuildhall) {
                'Guildhall process (unregistered checkout)'
            } else {
                'unverified owner; left untouched'
            }
            $results += [pscustomobject]@{
                Port = $port
                PID = $ownerPid
                Process = $procName
                IsGuildhall = [bool]$isAnyGuildhall
                Checkout = $checkoutLabel
                SessionId = if ($matchedSession) { $matchedSession.SessionId } else { $null }
                Ownership = $ownership
            }
        }
    }
    return @($results)
}

function Stop-VerifiedGuildhallPortListeners {
    param([int[]]$Ports = @(43120, 1420))
    $listeners = Get-VerifiedGuildhallPortListeners -Ports $Ports | Where-Object { $_.IsGuildhall -and $_.PID -ne $PID }
    foreach ($item in $listeners) {
        $live = Get-Process -Id $item.PID -ErrorAction SilentlyContinue
        if ($live) {
            try { $live.Kill($true); $live.WaitForExit(3000) | Out-Null } catch { }
        }
    }
}

function Assert-LocalPortFree {
    param([int]$Port)
    if (Test-LocalPortFree $Port) { return }
    $owner = Get-RegisteredDevSessions | Where-Object {
        $_.Session.hostPort -eq $Port -or $_.Session.frontendPort -eq $Port
    } | Select-Object -First 1
    if ($owner) {
        throw "Loopback port $Port is occupied by Guildhall session $($owner.SessionId) in '$($owner.Session.checkout)'. Stop it with 'pwsh -NoProfile -File .\scripts\startup\Stop-Dev.ps1 -All', re-run Start-Dev.ps1 with '-Replace', or pass '-AutoPort' to run side-by-side."
    }
    throw "Loopback port $Port is occupied. Inspect active listeners with 'pwsh -NoProfile -File .\scripts\startup\Get-DevStatus.ps1' or pass '-AutoPort' to pick a free port."
}

function New-ChildEnvironment {
    # Allowlist build/OS requirements; never inherit arbitrary API keys or SQL credentials.
    $values = @{}
    $names = @('PATH','PATHEXT','SystemRoot','WINDIR','COMSPEC','TEMP','TMP','USERPROFILE','APPDATA','LOCALAPPDATA',
        'ProgramFiles','ProgramFiles(x86)','ProgramW6432','SystemDrive','HOMEDRIVE','HOMEPATH','USERNAME','USERDOMAIN',
        'NUMBER_OF_PROCESSORS','PROCESSOR_ARCHITECTURE','CARGO_HOME','RUSTUP_HOME','DOTNET_ROOT','NVM_HOME','NVM_SYMLINK',
        'VCToolsInstallDir','VSINSTALLDIR','WindowsSdkDir','INCLUDE','LIB','LIBPATH')
    foreach ($name in $names) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($null -ne $value) { $values[$name] = $value }
    }
    $values['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    $values['NO_UPDATE_NOTIFIER'] = '1'
    return $values
}

function Get-SourceFingerprint {
    $sourceRoots = @('apps/desktop/src', 'apps/desktop/src-tauri/src', 'services/agent-host', 'scripts') |
        ForEach-Object { Join-Path $RepoRoot $_ } | Where-Object { Test-Path -LiteralPath $_ }
    $files = Get-ChildItem -LiteralPath $sourceRoots -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules|target|dist)[\\/]' } |
        Sort-Object FullName
    $parts = foreach ($file in $files) {
        $relative = [System.IO.Path]::GetRelativePath($RepoRoot, $file.FullName).Replace('\','/')
        "$relative $((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)"
    }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($parts -join "`n"))
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Start-OwnedChild {
    param([string]$Executable, [string[]]$Arguments, [hashtable]$ChildEnvironment, [string]$WorkingDirectory = $RepoRoot)
    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $Executable
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.Environment.Clear()
    foreach ($key in $ChildEnvironment.Keys) { $info.Environment[$key] = [string]$ChildEnvironment[$key] }
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    return [System.Diagnostics.Process]::Start($info)
}

function Stop-OwnedChild {
    param([System.Diagnostics.Process]$Process)
    if ($null -ne $Process) {
        try { if (-not $Process.HasExited) { $Process.Kill($true); $Process.WaitForExit(5000) | Out-Null } }
        catch [System.InvalidOperationException] { }
        finally { $Process.Dispose() }
    }
}
