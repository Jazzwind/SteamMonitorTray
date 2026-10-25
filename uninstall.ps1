$ErrorActionPreference = 'Stop'
$app = Join-Path $PSScriptRoot 'SteamMonitorTray.exe'
if (!(Test-Path -LiteralPath $app)) { $app = Join-Path $PSScriptRoot 'bin\SteamMonitorTray.exe' }
if (!(Test-Path -LiteralPath $app)) { throw 'SteamMonitorTray.exe was not found.' }
$process = Start-Process -FilePath $app -ArgumentList '--uninstall-startup' -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) { throw 'Could not remove the startup shortcut.' }
Write-Host 'Startup shortcut removed. Local settings, monitor configuration and history are preserved.'
Write-Host 'Use the tray Exit command to stop running monitors gracefully.'
