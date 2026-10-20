[CmdletBinding()]
param(
    [switch]$SkipAiInstall,
    [switch]$NoDesktopShortcut,
    [switch]$NoStartMenuShortcut
)

$ErrorActionPreference = 'Stop'

$packageRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$packageExe = Join-Path $packageRoot 'MissionControl.Desktop.exe'
$packageAiSetup = Join-Path $packageRoot 'scripts\Install-OriSmallMC.ps1'
$packageModelFile = Join-Path $packageRoot 'models\OriSmallMC.Modelfile'

if (-not (Test-Path -LiteralPath $packageExe -PathType Leaf)) {
    throw 'MissionControl.Desktop.exe is missing. Extract the complete Mission Control Windows ZIP, then run Install-MissionControl.ps1 from its top-level folder.'
}
if (-not (Test-Path -LiteralPath $packageAiSetup -PathType Leaf) -or
    -not (Test-Path -LiteralPath $packageModelFile -PathType Leaf)) {
    throw 'Ori Small MC setup files are missing. Extract the complete Mission Control Windows ZIP and try again.'
}

$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\Mission Control'
$installedExe = Join-Path $installDirectory 'MissionControl.Desktop.exe'
$installedAiSetup = Join-Path $installDirectory 'scripts\Install-OriSmallMC.ps1'
$startMenuDirectory = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Mission Control'
$desktopDirectory = [Environment]::GetFolderPath('Desktop')

$sourceFull = [System.IO.Path]::GetFullPath($packageRoot)
$installFull = [System.IO.Path]::GetFullPath($installDirectory)
$sameDirectory = [string]::Equals(
    $sourceFull.TrimEnd([char[]]@('\', '/')),
    $installFull.TrimEnd([char[]]@('\', '/')),
    [System.StringComparison]::OrdinalIgnoreCase
)

if (Get-Process -Name 'MissionControl.Desktop' -ErrorAction SilentlyContinue) {
    throw 'Please close Mission Control before installing or updating it, then run this installer again.'
}

Write-Host ''
Write-Host 'MISSION CONTROL + ORI SMALL MC' -ForegroundColor Cyan
Write-Host 'Installing for the current Windows user. Administrator access is not required.'
Write-Host ''

if (-not $sameDirectory) {
    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null

    # Copy the app and its supporting files, but not this package-level installer.
    Get-ChildItem -LiteralPath $packageRoot -Force |
        Where-Object { $_.Name -ne 'Install-MissionControl.ps1' } |
        ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $installDirectory -Recurse -Force
        }
}

if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) {
    throw "Mission Control files were copied, but the app executable is missing: $installedExe"
}

$shortcutShell = New-Object -ComObject WScript.Shell

if (-not $NoStartMenuShortcut) {
    New-Item -ItemType Directory -Path $startMenuDirectory -Force | Out-Null

    $startShortcutPath = Join-Path $startMenuDirectory 'Mission Control.lnk'
    $startShortcut = $shortcutShell.CreateShortcut($startShortcutPath)
    $startShortcut.TargetPath = $installedExe
    $startShortcut.WorkingDirectory = $installDirectory
    $startShortcut.IconLocation = "$installedExe,0"
    $startShortcut.Description = 'Mission Control space-launch dashboard'
    $startShortcut.Save()

    $aiShortcutPath = Join-Path $startMenuDirectory 'Install Ori Small MC.lnk'
    $aiShortcut = $shortcutShell.CreateShortcut($aiShortcutPath)
    $aiShortcut.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $aiShortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $installedAiSetup + '"'
    $aiShortcut.WorkingDirectory = $installDirectory
    $aiShortcut.Description = 'Install or repair the local Ori Small MC AI model'
    $aiShortcut.Save()
}

if (-not $NoDesktopShortcut) {
    if (-not $desktopDirectory) {
        Write-Warning 'Windows did not report a Desktop folder; the Start menu shortcut is still available.'
    }
    else {
        $desktopShortcutPath = Join-Path $desktopDirectory 'Mission Control.lnk'
        $desktopShortcut = $shortcutShell.CreateShortcut($desktopShortcutPath)
        $desktopShortcut.TargetPath = $installedExe
        $desktopShortcut.WorkingDirectory = $installDirectory
        $desktopShortcut.IconLocation = "$installedExe,0"
        $desktopShortcut.Description = 'Mission Control space-launch dashboard'
        $desktopShortcut.Save()
    }
}

Write-Host "Mission Control installed to: $installDirectory" -ForegroundColor Green
Write-Host 'Created a Start menu entry and desktop shortcut (unless disabled with command-line switches).'
Write-Host ''

if (-not $SkipAiInstall) {
    Write-Host 'Ori Small MC is the general-purpose chat assistant; it can discuss ordinary topics as well as space launches.'
    Write-Host 'Its first setup downloads Ollama/Qwen model data totaling several gigabytes.'
    Write-Host 'The model runs locally on this PC; this installer does not upload your chats.'
    $answer = Read-Host 'Install Ori Small MC now? Press Enter or type Y for yes, N to skip'
    if ($answer -notmatch '^(n|no)$') {
        try {
            & $installedAiSetup
            Write-Host 'Ori Small MC setup finished.' -ForegroundColor Green
        }
        catch {
            Write-Warning "Mission Control is installed, but Ori Small MC setup did not finish: $($_.Exception.Message)"
            Write-Warning "You can retry at any time from the Start menu entry 'Install Ori Small MC'."
        }
    }
    else {
        Write-Host "Skipped AI setup. Install it later from the Start menu entry 'Install Ori Small MC'." -ForegroundColor Yellow
    }
}
else {
    Write-Host "Skipped AI setup because -SkipAiInstall was specified. Use 'Install Ori Small MC' from the Start menu later." -ForegroundColor Yellow
}

Write-Host ''
Write-Host 'Launching Mission Control...' -ForegroundColor Cyan
Start-Process -FilePath $installedExe -WorkingDirectory $installDirectory
