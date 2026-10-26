param([switch]$Test, [switch]$BuildTests, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$bin = if ($OutputDirectory) { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory) } else { Join-Path $root 'bin' }
New-Item -ItemType Directory -Path $bin -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler is required.' }
function Compile([string[]]$CompilerArgs) {
    & $compiler /nologo /warn:4 /warnaserror+ @CompilerArgs
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
}
Compile @('/target:library', "/out:$bin\SteamMonitorTray.Core.dll", "/resource:$root\src\Translations.json,SteamMonitorTray.Translations.json", '/r:System.Runtime.Serialization.dll', '/r:System.Windows.Forms.dll', "$root\src\Core.cs", "$root\src\Native.cs", "$root\src\SessionBridge.cs", "$root\src\SessionClient.cs", "$root\src\Localization.cs")
Compile @('/target:exe', "/out:$bin\SteamMonitorTray.Session.exe", "/r:$bin\SteamMonitorTray.Core.dll", "$root\src\SessionHost.cs")
Compile @('/target:winexe', "/out:$bin\SteamMonitorTray.exe", "/win32manifest:$root\src\app.manifest", "/r:$bin\SteamMonitorTray.Core.dll", '/r:System.Runtime.Serialization.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', "$root\src\App.cs", "$root\src\SettingsForm.cs", "$root\src\Startup.cs", "$root\src\NativeMenu.cs", "$root\src\UiAssemblyInfo.cs")
Copy-Item -LiteralPath "$root\src\App.config" -Destination "$bin\SteamMonitorTray.exe.config" -Force
[IO.File]::WriteAllText((Join-Path $bin 'Session.ps1'), [IO.File]::ReadAllText((Join-Path $root 'scripts\Session.ps1')), (New-Object Text.UTF8Encoding($true)))
Copy-Item -LiteralPath "$root\scripts\InspectConfig.py" -Destination $bin -Force
if ($Test -or $BuildTests) {
    Compile @('/target:exe', "/out:$bin\FakeMonitor.exe", "$root\tests\FakeMonitor.cs")
    Compile @('/target:winexe', "/out:$bin\InputDriver.exe", "$root\tests\InputDriver.cs")
    $wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
    Compile @('/target:exe', "/out:$bin\SteamMonitorTray.Tests.exe", "/win32manifest:$root\src\app.manifest", "/r:$bin\SteamMonitorTray.Core.dll", "/r:$bin\SteamMonitorTray.exe", '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Runtime.Serialization.dll', "/r:$wpf\UIAutomationClient.dll", "/r:$wpf\UIAutomationTypes.dll", "$root\tests\Tests.cs", "$root\tests\ResizeTests.cs", "$root\tests\SettingsTests.cs", "$root\tests\StartupTests.cs", "$root\tests\LocalizationTests.cs", "$root\tests\NativeMenuTests.cs", "$root\tests\UiPreview.cs", "$root\src\UiAssemblyInfo.cs")
    Copy-Item -LiteralPath "$root\src\App.config" -Destination "$bin\SteamMonitorTray.Tests.exe.config" -Force
}
if ($Test) {
    & "$bin\SteamMonitorTray.Tests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
Write-Host "Built: $bin\SteamMonitorTray.exe"
