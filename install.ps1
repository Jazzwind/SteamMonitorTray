param([string]$ProfileDirectory, [switch]$NoStartup)
$ErrorActionPreference = 'Stop'
$app = Join-Path $PSScriptRoot 'SteamMonitorTray.exe'
if (!(Test-Path -LiteralPath $app)) { $app = Join-Path $PSScriptRoot 'bin\SteamMonitorTray.exe' }
if (!(Test-Path -LiteralPath $app)) { & (Join-Path $PSScriptRoot 'build.ps1') }
function Invoke-App([string]$Arguments) {
    $process = Start-Process -FilePath $app -ArgumentList $Arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw 'SteamMonitorTray operation failed.' }
}
if ($ProfileDirectory) {
    $directory = (Resolve-Path -LiteralPath $ProfileDirectory).Path.TrimEnd('\')
    Invoke-App ('--import-directory "' + $directory + '"')
}
if (!$NoStartup) { Invoke-App '--install-startup' }
Write-Host 'Installation complete. Run SteamMonitorTray.exe to start monitoring.'
