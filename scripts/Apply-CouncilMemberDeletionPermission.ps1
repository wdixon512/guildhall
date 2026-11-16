#Requires -Version 7.0
[CmdletBinding()]
param([switch]$Execute)
. "$PSScriptRoot/Common.ps1"

$databaseName = 'GuildhallDev'
$expectedPreviousMigration = '20261116021040_CouncilMemberArchive'
$targetMigration = '20261116134707_CouncilMemberDeletionPermission'
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

Write-Output "Target: localhost / $databaseName"
Write-Output "Migration: $targetMigration"
Write-Output 'Scope: grant DELETE only on dbo.AgentDefinitions to the existing guildhall_runtime principal. No schema or data changes.'
$previewPath = Join-Path $LocalDirectory "$databaseName.member-delete-permission.preview.sql"
if (-not (Test-Path -LiteralPath $LocalDirectory)) { New-Item -ItemType Directory -Path $LocalDirectory | Out-Null }
Push-Location $RepoRoot
try {
    Invoke-Checked dotnet @('ef','migrations','script',$expectedPreviousMigration,$targetMigration,'--project','services/agent-host','--startup-project','services/agent-host','--configuration','Release','--output',$previewPath)
} finally { Pop-Location }
Write-Output "Generated SQL preview: $previewPath"
if (-not $Execute) {
    Write-Output 'Preview only. No database changes were made.'
    Write-Output 'Run this script with -Execute only after approving docs/council-member-deletion-permission-approval.md.'
    return
}

$previous = $env:ConnectionStrings__GuildhallMigrations
Push-Location $RepoRoot
try {
    $env:ConnectionStrings__GuildhallMigrations = $connection
    Invoke-Checked dotnet @('ef','database','update',$targetMigration,'--project','services/agent-host','--startup-project','services/agent-host','--configuration','Release')
} finally { $env:ConnectionStrings__GuildhallMigrations = $previous; Pop-Location }
