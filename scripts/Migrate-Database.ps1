#Requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('GuildhallDev')][string]$DatabaseName = 'GuildhallDev', [switch]$Execute)
. "$PSScriptRoot/Common.ps1"
Write-Output "Migration target: localhost / $DatabaseName; Windows identity; encrypted local connection."
$connection = Get-LocalSqlConnection -Database $DatabaseName
$db = [System.Data.SqlClient.SqlConnection]::new($connection)
try {
    $db.Open() # Refuse implicit database creation.
    $command = $db.CreateCommand()
    $command.CommandText = "IF OBJECT_ID(N'dbo.__EFMigrationsHistory') IS NOT NULL SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;"
    $reader = $command.ExecuteReader()
    $applied = @(); while ($reader.Read()) { $applied += $reader.GetString(0) }
    $reader.Close()
} finally { $db.Dispose() }
$migrations = @(Get-ChildItem (Join-Path $RepoRoot 'services/agent-host/Persistence/Migrations') -File |
    Where-Object Name -Match '^\d{14}_[A-Za-z0-9_]+\.cs$' | Sort-Object Name | ForEach-Object BaseName)
if (-not $migrations.Count) { throw 'No source migrations found.' }
if ($applied.Count -gt $migrations.Count -or (($applied -join ',') -ne (($migrations | Select-Object -First $applied.Count) -join ','))) {
    throw 'Database migration history differs from this checkout; review before continuing.'
}
$pending = @($migrations | Select-Object -Skip $applied.Count)
if (-not $pending.Count) { Write-Output 'No pending migrations. No database changes made.'; return }
$from = if ($applied.Count) { $applied[-1] } else { '0' }
$target = $migrations[-1]
Write-Output "Pending migrations: $($pending -join ', ')"
$previewPath = Join-Path $LocalDirectory "$DatabaseName.migrations.preview.sql"
New-Item -ItemType Directory -Path $LocalDirectory -Force | Out-Null
$previous = $env:ConnectionStrings__GuildhallMigrations
Push-Location $RepoRoot
try {
    $env:ConnectionStrings__GuildhallMigrations = $connection
    # Release output is separate from the running development host's locked Debug executable.
    Invoke-Checked dotnet @('build','services/agent-host','--configuration','Release','--verbosity','quiet')
    $efArguments = @('--project','services/agent-host','--startup-project','services/agent-host','--configuration','Release','--no-build')
    Invoke-Checked dotnet (@('ef','migrations','script',$from,$target,'--output',$previewPath) + $efArguments)
    Write-Output "SQL preview: $previewPath"
    Get-Content -LiteralPath $previewPath
    if (-not $Execute) {
        Write-Output 'Preview only. No database changes made. After approval, rerun with -Execute.'
        return
    }
    Invoke-Checked dotnet (@('ef','database','update',$target) + $efArguments)
} finally { $env:ConnectionStrings__GuildhallMigrations = $previous; Pop-Location }
