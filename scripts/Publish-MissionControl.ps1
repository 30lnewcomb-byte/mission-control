[CmdletBinding()]
param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repoRoot 'src\MissionControl.Desktop\MissionControl.Desktop.csproj'
$artifactsDirectory = Join-Path $repoRoot 'artifacts'
$outputDirectory = Join-Path $artifactsDirectory 'MissionControl'
$archivePath = Join-Path $artifactsDirectory 'MissionControl-win-x64.zip'

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw 'The .NET SDK was not found. Install .NET 8 SDK and run this script again.'
}

if (-not (Test-Path $projectPath)) {
    throw "Desktop project was not found: $projectPath"
}

if (-not (Test-Path (Join-Path $repoRoot 'models\OriSmallMC.Modelfile'))) {
    throw 'Ori Small MC Modelfile was not found. Run the script from a complete Mission Control checkout.'
}

New-Item -ItemType Directory -Path $artifactsDirectory -Force | Out-Null
if (Test-Path $outputDirectory) {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}
if (Test-Path $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

Write-Host ''
Write-Host 'Mission Control - Windows preview publisher' -ForegroundColor Cyan
Write-Host 'Publishing a self-contained Windows x64 desktop build...' -ForegroundColor Cyan
Write-Host 'The package includes the unified installer; the multi-gigabyte model weights are fetched during AI setup.'
Write-Host ''

$publishArgs = @(
    'publish'
    $projectPath
    '--configuration'
    'Release'
    '--runtime'
    $Runtime
    '--self-contained'
    'true'
    '-p:PublishSingleFile=true'
    '-p:EnableCompressionInSingleFile=true'
    '-p:DebugType=None'
    '-p:DebugSymbols=false'
    '--output'
    $outputDirectory
)
& $dotnet.Source @publishArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$aiScriptDirectory = Join-Path $outputDirectory 'scripts'
$modelDirectory = Join-Path $outputDirectory 'models'
New-Item -ItemType Directory -Path $aiScriptDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $modelDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts\Install-OriSmallMC.ps1') -Destination (Join-Path $aiScriptDirectory 'Install-OriSmallMC.ps1') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'models\OriSmallMC.Modelfile') -Destination (Join-Path $modelDirectory 'OriSmallMC.Modelfile') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts\Install-MissionControl.ps1') -Destination (Join-Path $outputDirectory 'Install-MissionControl.ps1') -Force

$bundleReadme = @'
MISSION CONTROL - WINDOWS X64 PREVIEW

1. Extract this ZIP to a folder you control.
2. Open PowerShell in that extracted folder and run:
       .\Install-MissionControl.ps1
3. The installer copies Mission Control into your user Programs folder and
   creates Start menu and desktop shortcuts. Administrator access is not needed.
4. The installer offers to install Ori Small MC too. Its first setup downloads
   Ollama and the Qwen3.5 4B base model (several gigabytes); press Enter to
   accept the default Yes, or type N to skip. The model weights are not bundled
   in this ZIP. If skipped or if setup fails, use the Start menu entry
   "Install Ori Small MC" to retry later.
5. Ori Small MC is a general-purpose local chat assistant, not just a launch bot.
   Chats are sent to the Ollama service on this PC; Mission Control does not
   upload them to a remote AI API.
6. This is still an early preview. A real Windows Widgets provider, weather
   integration, official live coverage/status adapters, signed installer,
   updater, and uninstaller are not included yet.

Countdown math is local and deterministic. A scheduled time passing does not
confirm liftoff; this preview has no authoritative NASA/SpaceX liftoff adapter.
'@

Set-Content -LiteralPath (Join-Path $outputDirectory 'README.txt') -Value $bundleReadme -Encoding utf8
Compress-Archive -Path (Join-Path $outputDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal -Force

if (-not (Test-Path $archivePath) -or (Get-Item $archivePath).Length -le 0) {
    throw 'The preview ZIP was not created or is empty.'
}

Write-Host ''
Write-Host 'Preview package created:' -ForegroundColor Green
Write-Host $archivePath
Write-Host 'This is a preview build, not the final installer.' -ForegroundColor Yellow
