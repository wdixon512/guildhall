#Requires -Version 7.0
[CmdletBinding()]
param([switch]$Execute)
. "$PSScriptRoot/../misc/Common.ps1"

if (-not $IsWindows) { throw 'Phase 0 credential provisioning requires Windows DPAPI.' }
if (-not $Execute) {
    Write-Output 'PLAN ONLY: localhost; create GuildhallDev and guildhall_runtime; Windows migrations; runtime SELECT on metadata/history only; DPAPI credential shared across worktrees in LocalAppData. See docs/masterplan/phase 0/setup-approval.md. Pass -Execute only after approval.'
    return
}
New-Item -ItemType Directory -Path (Split-Path -Parent $CredentialPath) -Force | Out-Null
if (Test-Path -LiteralPath $CredentialPath) { throw 'Runtime credential already exists; refusing to replace it.' }
Push-Location $RepoRoot
try {
    $schemaPath = Join-Path $LocalDirectory 'migrations.sql'
    Invoke-Checked dotnet @('ef','migrations','script','--project','services/agent-host','--startup-project','services/agent-host','--idempotent','--output',$schemaPath)
    $master = [System.Data.SqlClient.SqlConnection]::new('Server=localhost;Database=master;Integrated Security=true;Encrypt=true;TrustServerCertificate=true;Connect Timeout=3')
    try {
        $master.Open()
        $check = $master.CreateCommand()
        $check.CommandText = "SELECT CASE WHEN DB_ID(N'GuildhallDev') IS NOT NULL OR SUSER_ID(N'guildhall_runtime') IS NOT NULL THEN 1 ELSE 0 END"
        if ([int]$check.ExecuteScalar() -ne 0) { throw 'Database or login already exists; refusing to modify it.' }
        $password = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) + '!aA1'
        $credential = [pscredential]::new('guildhall_runtime', (ConvertTo-SecureString -String $password -AsPlainText -Force))
        # Windows Export-Clixml protects the password with DPAPI for this OS user/machine.
        $credential | Export-Clixml -LiteralPath $CredentialPath
        $command = $master.CreateCommand()
        $command.CommandText = 'CREATE DATABASE [GuildhallDev]'
        $command.ExecuteNonQuery() | Out-Null
        $command.CommandText = "DECLARE @sql nvarchar(max) = N'CREATE LOGIN [guildhall_runtime] WITH PASSWORD = ' + QUOTENAME(@password, '''') + N', CHECK_POLICY = ON, DEFAULT_DATABASE = [GuildhallDev]'; EXEC sys.sp_executesql @sql;"
        $command.Parameters.Add('@password', [System.Data.SqlDbType]::NVarChar, 128).Value = $password
        $command.ExecuteNonQuery() | Out-Null
    } finally { $master.Dispose(); $password = $null }

    & "$PSScriptRoot/Migrate-Database.ps1" -DatabaseName GuildhallDev -Execute
    $database = [System.Data.SqlClient.SqlConnection]::new((Get-LocalSqlConnection))
    try {
        $database.Open()
        $command = $database.CreateCommand()
        $command.CommandText = @'
CREATE USER [guildhall_runtime] FOR LOGIN [guildhall_runtime];
GRANT CONNECT TO [guildhall_runtime];
GRANT SELECT ON OBJECT::dbo.ApplicationMetadata TO [guildhall_runtime];
GRANT SELECT ON OBJECT::dbo.__EFMigrationsHistory TO [guildhall_runtime];
DENY ALTER, TAKE OWNERSHIP, CREATE TABLE, CREATE VIEW, CREATE PROCEDURE TO [guildhall_runtime];
'@
        $command.ExecuteNonQuery() | Out-Null
    } finally { $database.Dispose() }
    Write-Output 'GuildhallDev provisioned. Runtime credential saved with Windows DPAPI. No credential was printed.'
} catch {
    Write-Warning 'Provisioning did not complete. Any created Guildhall objects and saved credential were preserved for inspection; no automatic destructive cleanup was attempted.'
    throw
} finally { Pop-Location }
