#Requires -Version 7.0
[CmdletBinding()]
param([switch]$Execute)
. "$PSScriptRoot/../misc/Common.ps1"
$databaseName = 'GuildhallDev'
$targetMigration = '20261116012111_CouncilProfiles'
$expectedPreviousMigration = '20261115231118_ChatAttachments'
$connection = Get-LocalSqlConnection -Database $databaseName
$db = [System.Data.SqlClient.SqlConnection]::new($connection)
try {
    $db.Open() # Refuse implicit database creation.
    $command = $db.CreateCommand()
    $command.CommandText = "SELECT DB_NAME(), (SELECT TOP (1) [MigrationId] FROM [dbo].[__EFMigrationsHistory] ORDER BY [MigrationId] DESC);"
    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) { throw 'Could not read the GuildhallDev migration state.' }
    $actualDatabase = [string]$reader.GetValue(0)
    $actualMigration = if ($reader.IsDBNull(1)) { '' } else { [string]$reader.GetValue(1) }
    $reader.Close()
    if ($actualDatabase -ne $databaseName) { throw "Refusing target database '$actualDatabase'." }
    if ($actualMigration -eq $targetMigration) { Write-Output "GuildhallDev already has $targetMigration; no changes made."; return }
    if ($actualMigration -ne $expectedPreviousMigration) { throw "Expected $expectedPreviousMigration but found '$actualMigration'. Review the database plan before continuing." }
} finally { $db.Dispose() }
Write-Output "Target: localhost / GuildhallDev"
Write-Output "Migration: $targetMigration"
if (-not $Execute) {
    Write-Output 'Preview only. No database changes were made.'
    Write-Output 'After approving docs/masterplan/phase 1/council-profile-migration-approval.md, run: pwsh -NoProfile -File G:\repos\guildhall\scripts\database\Apply-CouncilProfiles.ps1 -Execute'
    return
}
$previous = $env:ConnectionStrings__GuildhallMigrations
Push-Location $RepoRoot
try {
    $env:ConnectionStrings__GuildhallMigrations = $connection
    Invoke-Checked dotnet @('ef','database','update',$targetMigration,'--project','services/agent-host','--startup-project','services/agent-host','--configuration','Release')
} finally { $env:ConnectionStrings__GuildhallMigrations = $previous; Pop-Location }
