#Requires -Version 7.0
[CmdletBinding()]
param()
. "$PSScriptRoot/Common.ps1"
Assert-LocalPortFree 43120
$dll = Join-Path $RepoRoot 'services/agent-host/bin/Debug/net10.0/Guildhall.AgentHost.dll'
$runtimeConnection = Get-LocalSqlConnection -Runtime
if (-not $runtimeConnection) { throw 'Approved SQL provisioning is required for the live smoke check.' }
$installationIds = [System.Collections.Generic.List[guid]]::new()
for ($attempt = 0; $attempt -lt 2; $attempt++) {
    $childEnvironment = New-ChildEnvironment
    $token = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $childEnvironment['GUILDHALL_SESSION_TOKEN'] = $token
    $childEnvironment['GUILDHALL_HOST_URL'] = 'http://127.0.0.1:43120'
    $childEnvironment['ConnectionStrings__Guildhall'] = $runtimeConnection
    $process = $null
    try {
        $process = Start-OwnedChild (Get-Command dotnet).Source @($dll) $childEnvironment
        $deadline = [datetime]::UtcNow.AddSeconds(10)
        $health = $null
        while ([datetime]::UtcNow -lt $deadline) {
            if ($process.HasExited) { throw 'Smoke host exited during startup.' }
            try { $health = Invoke-RestMethod 'http://127.0.0.1:43120/health' -Headers @{Authorization="Bearer $token"} -TimeoutSec 1; break } catch { }
            Start-Sleep -Milliseconds 100
        }
        if (-not $health -or $health.status -ne 'ok' -or $health.apiVersion -ne 1) { throw 'Host liveness failed.' }
        $ready = Invoke-RestMethod 'http://127.0.0.1:43120/ready' -Headers @{Authorization="Bearer $token"} -TimeoutSec 5
        if ($ready.status -ne 'ready') { throw 'SQL readiness failed.' }
        $models = Invoke-RestMethod 'http://127.0.0.1:43120/models' -Headers @{Authorization="Bearer $token"} -TimeoutSec 5
        if ($models.providerId -ne 'ollama' -or $models.status -ne 'available' -or @($models.models).Count -eq 0) { throw 'Live local model discovery failed.' }
        $denied = Invoke-WebRequest 'http://127.0.0.1:43120/health' -SkipHttpErrorCheck -TimeoutSec 2
        if ($denied.StatusCode -ne 401) { throw 'Unauthenticated request was not rejected.' }
        $origin = Invoke-WebRequest 'http://127.0.0.1:43120/health' -Headers @{Authorization="Bearer $token"; Origin='https://unrelated.example'} -SkipHttpErrorCheck -TimeoutSec 2
        if ($origin.StatusCode -ne 403) { throw 'Unrelated origin was not rejected.' }
        $connection = [System.Data.SqlClient.SqlConnection]::new($runtimeConnection)
        try {
            $connection.Open(); $command = $connection.CreateCommand()
            $command.CommandText = 'SELECT InstallationId FROM dbo.ApplicationMetadata WHERE Id = 1'
            $installationIds.Add([guid]$command.ExecuteScalar())
        } finally { $connection.Dispose() }
        Write-Output "Live host run $($attempt + 1): health, SQL readiness, discovery, authentication, and origin checks passed."
    } finally { Stop-OwnedChild $process; $token = $null }
}
if ($installationIds[0] -ne $installationIds[1]) { throw 'Installation metadata changed across restart.' }
Assert-LocalPortFree 43120
Write-Output 'Restart preserved SQL installation metadata; owned host listener cleaned up.'
