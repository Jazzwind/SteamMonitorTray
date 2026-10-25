param([string]$Version = '1.2.0')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid version.' }
$stage = Join-Path $PSScriptRoot ('dist\staging\' + [Guid]::NewGuid().ToString('N'))
$buildBin = Join-Path $stage 'build'
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $buildBin
& python (Join-Path $PSScriptRoot 'scripts\ScanPrivacy.py')
if ($LASTEXITCODE -ne 0) { throw 'Privacy scan failed.' }
$folder = Join-Path $stage ("SteamMonitorTray-$Version-portable")
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$runtimeFiles = 'SteamMonitorTray.exe','SteamMonitorTray.exe.config','SteamMonitorTray.Core.dll','SteamMonitorTray.Session.exe','Session.ps1','InspectConfig.py'
# Explicit allowlist: never package all of bin or any runtime/config directory.
foreach ($name in $runtimeFiles) { Copy-Item -LiteralPath (Join-Path $buildBin $name) -Destination $folder -Force }
foreach ($name in @('README.md','README.en.md','LICENSE','install.ps1','uninstall.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $folder -Force }
$archive = Join-Path $PSScriptRoot ("dist\SteamMonitorTray-$Version-portable.zip")
Compress-Archive -LiteralPath $folder -DestinationPath $archive -Force
& python (Join-Path $PSScriptRoot 'scripts\ScanPrivacy.py') --archive $archive
if ($LASTEXITCODE -ne 0) { throw 'Archive privacy scan failed.' }
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Format-List
