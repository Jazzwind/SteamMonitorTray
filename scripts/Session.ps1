param([Parameter(Mandatory=$true)][string]$SessionFile)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'SteamMonitorTray.Core.dll')
$global:SmtSession = Get-Content -LiteralPath $SessionFile -Raw -Encoding UTF8 | ConvertFrom-Json
[SteamMonitorTray.L]::SetLanguage($global:SmtSession.Language)
[SteamMonitorTray.SessionBridge]::Connect($global:SmtSession.ShellPipe, $global:SmtSession.Token)
Set-Location -LiteralPath $global:SmtSession.Profile.WorkingDirectory
Import-Module PSReadLine -ErrorAction Stop
# Process-local settings: do not change the user's profile or persist command history.
Set-PSReadLineOption -HistorySaveStyle SaveNothing -AddToHistoryHandler {
    param([string]$line)
    [SteamMonitorTray.SessionBridge]::Busy()
    return $true
}
$global:SmtOriginalPrompt = (Get-Command prompt).ScriptBlock
function global:prompt {
    [SteamMonitorTray.SessionBridge]::Prompt()
    & $global:SmtOriginalPrompt
}
function global:steam_monitor {
    $monitorArguments = @($args)
    $oneShot = @('--setup','--doctor','--help','-h','--version','--generate-config','--set-steam-api-key','--set-webhook-url','--set-smtp-password','--send-test-email','--send-test-webhook','-i','--info','-r','--resolve-community-url')
    $managed = $true
    foreach ($argument in $monitorArguments) {
        if ($oneShot -contains $argument) { $managed = $false }
    }
    $prefix = @()
    if (!($monitorArguments -contains '--config-file')) { $prefix += @('--config-file', $global:SmtSession.Profile.ConfigFile) }
    if (!($monitorArguments -contains '--env-file')) {
        $envPath = $global:SmtSession.Profile.EnvFile
        if ([string]::IsNullOrEmpty($envPath)) { $envPath = 'none' }
        $prefix += @('--env-file', $envPath)
    }
    $result = 0
    if ($managed) { [SteamMonitorTray.SessionBridge]::Started() }
    try {
        & $global:SmtSession.Profile.Executable @prefix @monitorArguments
        $result = $LASTEXITCODE
    }
    catch [System.Management.Automation.PipelineStoppedException] { $result = 0; throw }
    catch { $result = 1; Write-Error -ErrorRecord $_ -ErrorAction Continue }
    finally { if ($managed) { [SteamMonitorTray.SessionBridge]::Stopped([int]$result) } }
}
function global:Invoke-SteamMonitorTrayStart { steam_monitor }
Set-PSReadLineKeyHandler -Chord F24 -BriefDescription SteamMonitorTray -ScriptBlock {
    $request = [SteamMonitorTray.SessionBridge]::TakeRequest()
    if (!$request) { return }
    $line = ''; $cursor = 0
    [Microsoft.PowerShell.PSConsoleReadLine]::GetBufferState([ref]$line, [ref]$cursor)
    if ($line.Length -ne 0) { [SteamMonitorTray.SessionBridge]::RejectInput(); return }
    if ($request -eq 'Start') {
        [Microsoft.PowerShell.PSConsoleReadLine]::Insert('Invoke-SteamMonitorTrayStart')
    }
    elseif ($request -eq 'Exit') {
        [SteamMonitorTray.SessionBridge]::Exiting()
        [Microsoft.PowerShell.PSConsoleReadLine]::Insert('exit')
    }
    [Microsoft.PowerShell.PSConsoleReadLine]::AcceptLine()
}
Write-Host ([SteamMonitorTray.L]::Text('SteamMonitorTray — 使用 steam_monitor 启动，Ctrl+C 停止，最小化收进托盘。', @())) -ForegroundColor Cyan
