#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$LocalDirectory = Join-Path $RepoRoot '.local'
$LegacyCredentialPath = Join-Path $LocalDirectory 'runtime.credential.xml'
$SharedCredentialDirectory = if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Guildhall/dev' } else { Join-Path $HOME '.local/share/Guildhall/dev' }
$SharedCredentialPath = Join-Path $SharedCredentialDirectory 'runtime.credential.xml'
# Prefer one user-local credential across worktrees, while preserving existing checkout-local setups.
$CredentialPath = if (Test-Path -LiteralPath $SharedCredentialPath) { $SharedCredentialPath } elseif (Test-Path -LiteralPath $LegacyCredentialPath) { $LegacyCredentialPath } else { $SharedCredentialPath }

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
        if (-not (Test-Path -LiteralPath $CredentialPath)) { return $null }
        $credential = Import-Clixml -LiteralPath $CredentialPath
        if ($credential -isnot [pscredential] -or $credential.UserName -ne 'guildhall_runtime') { throw 'Invalid saved runtime credential.' }
        $builder['User ID'] = $credential.UserName
        $builder['Password'] = $credential.GetNetworkCredential().Password
    } else { $builder['Integrated Security'] = $true }
    return $builder.ConnectionString
}

function Assert-LocalPortFree {
    param([int]$Port)
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
    try { $listener.Start() } catch { throw "Loopback port $Port is occupied. No process was stopped." }
    finally { $listener.Stop() }
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
