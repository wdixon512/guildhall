#Requires -Version 7.0
[CmdletBinding()]
param([switch]$IncludeSql)
. "$PSScriptRoot/Common.ps1"
Push-Location $RepoRoot
try {
    Invoke-Checked dotnet @('build','Guildhall.slnx','--no-restore')
    Invoke-Checked dotnet @('test','tests/Guildhall.AgentHost.Tests','--no-build','--filter','Category!=SqlIntegration')
    foreach ($task in @('typecheck','build')) { Invoke-Checked npm.cmd @('--prefix','apps/desktop','run',$task) }
    Invoke-Checked npm.cmd @('--prefix','apps/desktop','run','test','--','--run')
    Invoke-Checked cargo @('test','--locked','--manifest-path','apps/desktop/src-tauri/Cargo.toml')
    if ($IncludeSql) {
        if (-not $env:GUILDHALL_TEST_SQL_CONNECTION) { throw 'Set the explicitly approved GUILDHALL_TEST_SQL_CONNECTION before running SQL tests.' }
        Invoke-Checked dotnet @('test','tests/Guildhall.AgentHost.Tests','--no-build','--filter','Category=SqlIntegration')
    } else { Write-Warning 'SQL integration checks NOT RUN. This is not a complete Phase 0 verification.' }
    Write-Output 'Selected automated checks passed. Native desktop and live-model acceptance checks are separate.'
} finally { Pop-Location }
