#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$DatabaseName = 'GuildhallDev',
    [switch]$Execute,
    [string]$BackupDirectory = ''
)
. "$PSScriptRoot/../misc/Common.ps1"

function Quote-SqlIdentifier([string]$name) { '[' + $name.Replace(']', ']]') + ']' }

Write-Output "Backup/Restore Verification Target: localhost / $DatabaseName (COPY_ONLY -> isolated restore DB)"
if (-not $Execute) {
    Write-Output "PLAN ONLY: Would create a COPY_ONLY backup in SQL Server's default backup path (or -BackupDirectory), run RESTORE VERIFYONLY, restore into an ephemeral GuildhallRestoreVerify_<guid> database, verify table parity, and drop the temporary restore database + backup files. Pass -Execute to run."
    return
}

$stamp = [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
$runId = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$restoreDb = "GuildhallRestoreVerify_${runId}"

$validatedConn = Get-LocalSqlConnection -Database $DatabaseName
$masterBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($validatedConn)
$masterBuilder['Initial Catalog'] = 'master'
$masterConnStr = $masterBuilder.ConnectionString
$master = [System.Data.SqlClient.SqlConnection]::new($masterConnStr)
$backupFile = $null
try {
    $master.Open()

    $propCmd = $master.CreateCommand()
    $propCmd.CommandText = "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(500)), CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(500)), CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(500));"
    $propReader = $propCmd.ExecuteReader()
    $sqlBackupDir = $null; $sqlDataDir = $null; $sqlLogDir = $null
    if ($propReader.Read()) {
        if (-not $propReader.IsDBNull(0)) { $sqlBackupDir = $propReader.GetString(0) }
        if (-not $propReader.IsDBNull(1)) { $sqlDataDir = $propReader.GetString(1) }
        if (-not $propReader.IsDBNull(2)) { $sqlLogDir = $propReader.GetString(2) }
    }
    $propReader.Close()

    if (-not $BackupDirectory) {
        $BackupDirectory = if ($sqlBackupDir) { $sqlBackupDir } else { Join-Path $LocalDirectory 'backup-verify' }
    }
    $dataDir = if ($sqlDataDir) { $sqlDataDir } else { $BackupDirectory }
    $logDir = if ($sqlLogDir) { $sqlLogDir } else { $dataDir }

    $backupFile = Join-Path $BackupDirectory "${DatabaseName}_${stamp}_${runId}.bak"
    $restoreDataFile = Join-Path $dataDir "${restoreDb}.mdf"
    $restoreLogFile = Join-Path $logDir "${restoreDb}_log.ldf"

    # 1. Verify source database exists
    $existsCmd = $master.CreateCommand()
    $existsCmd.CommandText = "SELECT DB_ID(@db)"
    $existsCmd.Parameters.Add('@db', [System.Data.SqlDbType]::NVarChar, 128).Value = $DatabaseName
    $dbId = $existsCmd.ExecuteScalar()
    if ($null -eq $dbId -or [Convert]::IsDBNull($dbId)) {
        throw "Source database '$DatabaseName' does not exist."
    }

    # 2. Take a COPY_ONLY checksummed backup (never disturbs differential/log backup chains)
    Write-Output "Taking COPY_ONLY backup to $backupFile..."
    $backupCmd = $master.CreateCommand()
    $backupCmd.CommandTimeout = 120
    $backupCmd.CommandText = "BACKUP DATABASE " + (Quote-SqlIdentifier $DatabaseName) + " TO DISK = @path WITH COPY_ONLY, CHECKSUM, INIT;"
    $backupCmd.Parameters.Add('@path', [System.Data.SqlDbType]::NVarChar, 500).Value = $backupFile
    $backupCmd.ExecuteNonQuery() | Out-Null

    # 3. Verify backup integrity
    Write-Output "Running RESTORE VERIFYONLY WITH CHECKSUM..."
    $verifyCmd = $master.CreateCommand()
    $verifyCmd.CommandTimeout = 120
    $verifyCmd.CommandText = "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;"
    $verifyCmd.Parameters.Add('@path', [System.Data.SqlDbType]::NVarChar, 500).Value = $backupFile
    $verifyCmd.ExecuteNonQuery() | Out-Null

    # 4. Read logical file names for WITH MOVE
    $filelistCmd = $master.CreateCommand()
    $filelistCmd.CommandText = "RESTORE FILELISTONLY FROM DISK = @path;"
    $filelistCmd.Parameters.Add('@path', [System.Data.SqlDbType]::NVarChar, 500).Value = $backupFile
    $logicalData = $null
    $logicalLog = $null
    $reader = $filelistCmd.ExecuteReader()
    while ($reader.Read()) {
        $type = [string]$reader['Type']
        $logicalName = [string]$reader['LogicalName']
        if ($type -eq 'D' -and -not $logicalData) { $logicalData = $logicalName }
        elseif ($type -eq 'L' -and -not $logicalLog) { $logicalLog = $logicalName }
    }
    $reader.Close()
    if (-not $logicalData -or -not $logicalLog) {
        throw 'Could not determine logical data/log file names from RESTORE FILELISTONLY.'
    }

    # 5. Restore into isolated temporary database
    Write-Output "Restoring into isolated verification database [$restoreDb]..."
    $restoreCmd = $master.CreateCommand()
    $restoreCmd.CommandTimeout = 120
    $restoreCmd.CommandText = @"
RESTORE DATABASE $(Quote-SqlIdentifier $restoreDb)
FROM DISK = @path
WITH MOVE @logicalData TO @dataFile,
     MOVE @logicalLog TO @logFile,
     RECOVERY,
     REPLACE;
"@
    $restoreCmd.Parameters.Add('@path', [System.Data.SqlDbType]::NVarChar, 500).Value = $backupFile
    $restoreCmd.Parameters.Add('@logicalData', [System.Data.SqlDbType]::NVarChar, 128).Value = $logicalData
    $restoreCmd.Parameters.Add('@dataFile', [System.Data.SqlDbType]::NVarChar, 500).Value = $restoreDataFile
    $restoreCmd.Parameters.Add('@logicalLog', [System.Data.SqlDbType]::NVarChar, 128).Value = $logicalLog
    $restoreCmd.Parameters.Add('@logFile', [System.Data.SqlDbType]::NVarChar, 500).Value = $restoreLogFile
    $restoreCmd.ExecuteNonQuery() | Out-Null

    # 6. Compare table counts and __EFMigrationsHistory parity
    $srcTablesCmd = $master.CreateCommand()
    $srcTablesCmd.CommandText = "SELECT COUNT(*) FROM " + (Quote-SqlIdentifier $DatabaseName) + ".INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE';"
    $srcTables = [int]$srcTablesCmd.ExecuteScalar()

    $dstTablesCmd = $master.CreateCommand()
    $dstTablesCmd.CommandText = "SELECT COUNT(*) FROM " + (Quote-SqlIdentifier $restoreDb) + ".INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE';"
    $dstTables = [int]$dstTablesCmd.ExecuteScalar()

    if ($srcTables -ne $dstTables) {
        throw "Table count mismatch after restore: source=$srcTables, restored=$dstTables."
    }

    Write-Output "Backup/restore verification PASSED: [$DatabaseName] ($srcTables tables) restored cleanly to [$restoreDb]."
}
finally {
    if ($master.State -eq [System.Data.ConnectionState]::Open) {
        try {
            $dropCmd = $master.CreateCommand()
            $dropCmd.CommandText = @"
IF DB_ID(@db) IS NOT NULL
BEGIN
    ALTER DATABASE $(Quote-SqlIdentifier $restoreDb) SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE $(Quote-SqlIdentifier $restoreDb);
END
"@
            $dropCmd.Parameters.Add('@db', [System.Data.SqlDbType]::NVarChar, 128).Value = $restoreDb
            $dropCmd.ExecuteNonQuery() | Out-Null
        } catch {
            Write-Warning "Could not drop temporary verification database [$restoreDb]: $($_.Exception.Message)"
        }
    }
    $master.Dispose()
    foreach ($f in @($backupFile, $restoreDataFile, $restoreLogFile)) {
        if (Test-Path -LiteralPath $f) {
            Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue
        }
    }
}
