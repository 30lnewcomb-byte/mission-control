[CmdletBinding()]
param(
    [switch]$SkipOllamaInstall
)

$ErrorActionPreference = 'Stop'
$ModelName = 'ori-small-mc'
$BaseUrl = 'http://localhost:11434'
$ModelfilePath = Join-Path $PSScriptRoot '..\models\OriSmallMC.Modelfile'
$ModelfilePath = [System.IO.Path]::GetFullPath($ModelfilePath)

function Find-Ollama {
    $cmd = Get-Command ollama -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'),
        (Join-Path $env:ProgramFiles 'Ollama\ollama.exe'),
        (Join-Path $env:LOCALAPPDATA 'Ollama\ollama.exe')
    ) | Where-Object { $_ -and (Test-Path $_) }

    if ($candidates) { return $candidates[0] }
    return $null
}

Write-Host ''
Write-Host 'Mission Control - Ori Small MC setup' -ForegroundColor Cyan
Write-Host 'This installs Ollama if needed, downloads Qwen3.5 4B, and creates the ori-small-mc model.'
Write-Host 'The model is stored locally by Ollama; this setup does not upload your chats.'
Write-Host ''

$ollamaExe = Find-Ollama
if (-not $ollamaExe) {
    if ($SkipOllamaInstall) {
        throw 'Ollama was not found. Install it from https://ollama.com/download/windows, then rerun this script.'
    }

    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw 'Ollama is missing and winget is unavailable. Install Ollama from https://ollama.com/download/windows, then rerun this script.'
    }

    Write-Host 'Installing Ollama with Windows Package Manager...' -ForegroundColor Yellow
    & $winget.Source install --id Ollama.Ollama --exact --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "Ollama installation failed with exit code $LASTEXITCODE. You can install it manually from https://ollama.com/download/windows."
    }

    $ollamaExe = Find-Ollama
    if (-not $ollamaExe) {
        throw 'Ollama was installed but its executable was not found in the usual locations. Restart PowerShell and rerun this script.'
    }
}

if (-not (Test-Path $ModelfilePath)) {
    throw "Modelfile was not found: $ModelfilePath. Run this script from the Mission Control repository or installed package."
}

$apiReady = $false
try {
    $null = Invoke-RestMethod -Uri "$BaseUrl/api/tags" -TimeoutSec 3
    $apiReady = $true
} catch {
    Write-Host 'Starting the local Ollama service...' -ForegroundColor Yellow
    Start-Process -FilePath $ollamaExe -ArgumentList 'serve' -WindowStyle Hidden
}

if (-not $apiReady) {
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Seconds 2
        try {
            $null = Invoke-RestMethod -Uri "$BaseUrl/api/tags" -TimeoutSec 3
            $apiReady = $true
            break
        } catch {
            # Keep waiting for the local service to start.
        }
    }
}
if (-not $apiReady) {
    throw 'Ollama did not become available at http://localhost:11434. Open Ollama and rerun this setup.'
}

Write-Host 'Downloading the Qwen3.5 4B base model (first run may take a while)...' -ForegroundColor Cyan
& $ollamaExe pull 'qwen3.5:4b'
if ($LASTEXITCODE -ne 0) {
    throw "The Qwen model download failed with exit code $LASTEXITCODE."
}

Write-Host 'Creating the persistent Ori Small MC model...' -ForegroundColor Cyan
& $ollamaExe create $ModelName -f $ModelfilePath
if ($LASTEXITCODE -ne 0) {
    throw "Creating $ModelName failed with exit code $LASTEXITCODE."
}

Write-Host ''
Write-Host 'Ori Small MC is installed locally.' -ForegroundColor Green
Write-Host 'Test it with: ollama run ori-small-mc'
Write-Host 'Mission Control can call: http://localhost:11434/api/chat'
Write-Host 'Model name: ori-small-mc'
