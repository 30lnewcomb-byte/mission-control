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
Write-Host 'Ori Small MC itself remains optional and is not bundled into this archive.'
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

$bundleReadme = @'
MISSION CONTROL - WINDOWS X64 PREVIEW

1. Extract the ZIP to a folder you control.
2. Run MissionControl.Desktop.exe.
3. The launch panel reads Launch Library 2 schedule metadata for launch sites
   explicitly identified as being in the United States.
4. Countdown math is local and deterministic. Schedule time passing does not
   confirm liftoff; this preview does not yet contain an authoritative
   NASA/SpaceX liftoff-status adapter.
5. Ori Small MC is optional. To install Ollama and the local model, open
   PowerShell in this extracted folder and run:
       .\scripts\Install-OriSmallMC.ps1
   That setup downloads several gigabytes and may take time. It is not required
   to open the dashboard.
6. This is an early desktop preview, not the final one-command installer.
   The real Windows Widgets provider, weather integration, official live
   coverage resolver, updater, and uninstaller are not included yet.

The app does not need administrator rights just to launch. Do not share private
files with the local model unless you intend to include that text in the chat.
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
