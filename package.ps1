param([string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid version.' }
& (Join-Path $PSScriptRoot 'build.ps1')
& python (Join-Path $PSScriptRoot 'scripts\ScanPrivacy.py')
if ($LASTEXITCODE -ne 0) { throw 'Privacy scan failed.' }
$stage = Join-Path $PSScriptRoot ('dist\staging\' + [Guid]::NewGuid().ToString('N'))
$folder = Join-Path $stage ("SteamMonitorTray-$Version-portable")
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$runtimeFiles = 'SteamMonitorTray.exe','SteamMonitorTray.Core.dll','SteamMonitorTray.Session.exe','Session.ps1','InspectConfig.py'
# Explicit allowlist: never package all of bin or any runtime/config directory.
foreach ($name in $runtimeFiles) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot "bin\$name") -Destination $folder -Force }
foreach ($name in @('README.md','LICENSE','install.ps1','uninstall.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $folder -Force }
$archive = Join-Path $PSScriptRoot ("dist\SteamMonitorTray-$Version-portable.zip")
Compress-Archive -LiteralPath $folder -DestinationPath $archive -Force
& python (Join-Path $PSScriptRoot 'scripts\ScanPrivacy.py') --archive $archive
if ($LASTEXITCODE -ne 0) { throw 'Archive privacy scan failed.' }
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Format-List
