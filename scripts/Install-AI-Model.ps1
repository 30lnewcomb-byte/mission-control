[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$OriPlatformSetup = Join-Path $PSScriptRoot 'Install-OriPlatformAI.ps1'
$QwenSetup = Join-Path $PSScriptRoot 'Install-OriSmallMC.ps1'

if (-not (Test-Path -LiteralPath $OriPlatformSetup -PathType Leaf)) {
    throw "Ori Platform setup script is missing: $OriPlatformSetup"
}
if (-not (Test-Path -LiteralPath $QwenSetup -PathType Leaf)) {
    throw "Qwen/Ollama setup script is missing: $QwenSetup"
}

Write-Host ''
Write-Host 'MISSION CONTROL · CHOOSE A LOCAL AI MODEL' -ForegroundColor Cyan
Write-Host ''
Write-Host '1. Ori Platform · native TensorFlow model' -ForegroundColor White
Write-Host '   Trains the Ori Platform model locally from its pinned source and training data.'
Write-Host '   Uses native Windows CPU TensorFlow. First-time setup may take a while.'
Write-Host ''
Write-Host '2. Ori Small MC · Qwen3.5 4B with Ollama' -ForegroundColor White
Write-Host '   Downloads the Qwen3.5 4B base model (several gigabytes).'
Write-Host ''
Write-Host 'Both choices run chat locally on this PC. You can install both and switch models in Mission Control.'
Write-Host ''

$choice = Read-Host 'Select 1 or 2 [default: 1]'
if ([string]::IsNullOrWhiteSpace($choice)) { $choice = '1' }

switch ($choice.Trim()) {
    '1' { & $OriPlatformSetup }
    '2' { & $QwenSetup }
    default { throw 'Invalid selection. Run this script again and choose 1 or 2.' }
}
