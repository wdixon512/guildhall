#Requires -Version 7.0
[CmdletBinding()]
param()
. "$PSScriptRoot/Common.ps1"
$failures = [System.Collections.Generic.List[string]]::new()
foreach ($name in @('node','npm.cmd','dotnet','cargo','rustc','sqlcmd')) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) { $failures.Add("Missing command: $name") }
}
if (Get-Command node -ErrorAction SilentlyContinue) {
    $version = (& node --version).TrimStart('v')
    Write-Output "Node $version"
    if ([version]$version -lt [version]'24.13.0' -or [version]$version -ge [version]'25.0.0') { $failures.Add('Use Node 24.13.0 or a newer Node 24 patch through nvm.') }
}
if ($IsWindows) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere) -or -not (& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)) { $failures.Add('Visual Studio C++ build tools not found.') }
    $kits = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' -ErrorAction SilentlyContinue
    if (-not $kits -or -not (Test-Path -LiteralPath (Join-Path $kits.KitsRoot10 'Include'))) { $failures.Add('Windows SDK headers not found.') }
    $webview = Join-Path ${env:ProgramFiles(x86)} 'Microsoft\EdgeWebView\Application'
    if (-not (Test-Path -LiteralPath $webview)) { $failures.Add('WebView2 runtime not found in the standard machine location; verify installation.') }
}
try {
    $models = Invoke-RestMethod -Uri 'http://127.0.0.1:11434/api/tags' -TimeoutSec 3
    if (@($models.models).Count -eq 0) { $failures.Add('Ollama is reachable but has no installed model.') }
    else { Write-Output "Ollama: $(@($models.models).Count) installed model(s)." }
} catch { $failures.Add('Ollama unavailable at 127.0.0.1:11434. Start/configure it separately; no services were changed.') }
if (-not (Test-Path -LiteralPath $CredentialPath)) { $failures.Add('SQL runtime credential has not been provisioned.') }
if ($failures.Count -gt 0) { foreach ($failure in $failures) { Write-Warning $failure }; throw 'Prerequisites incomplete.' }
Write-Output 'Prerequisite checks passed. No installations or service changes performed.'
