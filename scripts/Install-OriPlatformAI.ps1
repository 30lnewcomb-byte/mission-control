[CmdletBinding()]
param(
    [switch]$ForceRebuild
)

$ErrorActionPreference = 'Stop'
$OriPlatformCommit = '102f0c3819a1ab9b0aedbe4789d03c089431ee54'
$SourceArchiveUrl = "https://github.com/30lnewcomb-byte/ori-platform/archive/$OriPlatformCommit.zip"
$InstallRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$LocalRoot = Join-Path $InstallRoot 'ori-platform-local'
$ServiceRoot = Join-Path $LocalRoot 'services\intelligence'
$PythonVenv = Join-Path $LocalRoot '.venv'
$VenvPython = Join-Path $PythonVenv 'Scripts\python.exe'
$ModelDirectory = Join-Path $ServiceRoot 'artifacts\ori-small'
$LocalKeyPath = Join-Path $LocalRoot 'local-api-key.txt'
$InstallMarker = Join-Path $LocalRoot 'local-install.json'

function Test-OriPlatformModelInstalled {
    return (
        (Test-Path -LiteralPath (Join-Path $ModelDirectory 'config.json') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $ModelDirectory 'model.weights.h5') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $ModelDirectory 'ori_tokenizer.model') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $ServiceRoot 'runtime.py') -PathType Leaf) -and
        (Test-Path -LiteralPath $VenvPython -PathType Leaf)
    )
}

function Get-Python310 {
    $launcher = Get-Command py -ErrorAction SilentlyContinue
    if ($launcher) {
        try {
            $candidate = (& $launcher.Source -3.10 -c 'import sys; print(sys.executable)' 2>$null | Select-Object -First 1)
            if ($LASTEXITCODE -eq 0 -and $candidate -and (Test-Path -LiteralPath $candidate.Trim())) {
                return $candidate.Trim()
            }
        } catch {
            # Check the known install locations below.
        }
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python310\python.exe'),
        (Join-Path $env:ProgramFiles 'Python310\python.exe'),
        (Join-Path $env:ProgramFiles 'Python 3.10\python.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) }

    if ($candidates) { return $candidates[0] }
    return $null
}

Write-Host ''
Write-Host 'ORI PLATFORM AI · LOCAL TENSORFLOW SETUP' -ForegroundColor Cyan
Write-Host 'This makes a local copy of the Ori Platform model pipeline; it does not call or change the hosted Render service.'
Write-Host 'The source is pinned to the Ori Platform commit:' -ForegroundColor DarkGray
Write-Host $OriPlatformCommit -ForegroundColor DarkGray
Write-Host ''

if ((Test-OriPlatformModelInstalled) -and -not $ForceRebuild) {
    Write-Host 'The local Ori Platform model is already installed.' -ForegroundColor Green
    Write-Host 'To deliberately retrain it, rerun this script with -ForceRebuild.'
    exit 0
}

$winget = Get-Command winget -ErrorAction SilentlyContinue
$pythonExe = Get-Python310
if (-not $pythonExe) {
    if (-not $winget) {
        throw 'Python 3.10 x64 is required for native Windows TensorFlow. Install Python 3.10 x64, then rerun this script.'
    }

    Write-Host 'Installing Python 3.10 x64...' -ForegroundColor Yellow
    & $winget.Source install --id Python.Python.3.10 --exact --architecture x64 --scope user --silent --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "Python 3.10 installation failed with exit code $LASTEXITCODE. Install Python 3.10 x64, then rerun this script."
    }

    $pythonExe = Get-Python310
}
if (-not $pythonExe) {
    throw 'Python 3.10 was installed, but its executable could not be found. Close and reopen PowerShell, then run this script again.'
}

New-Item -ItemType Directory -Path $LocalRoot -Force | Out-Null

if (-not (Test-Path -LiteralPath $VenvPython -PathType Leaf)) {
    Write-Host 'Creating a private Python environment for Ori...' -ForegroundColor Cyan
    & $pythonExe -m venv $PythonVenv
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $VenvPython -PathType Leaf)) {
        throw 'Could not create the local Python environment.'
    }
}

Write-Host 'Installing native Windows CPU runtime dependencies...' -ForegroundColor Cyan
& $VenvPython -m pip install --disable-pip-version-check --upgrade pip
if ($LASTEXITCODE -ne 0) { throw 'Updating pip failed.' }

# TensorFlow 2.10.1 is the final native-Windows CPU release. The model is built
# from the pinned source revision and runs locally instead of using Render.
$dependencyArgs = @(
    '-m', 'pip', 'install', '--disable-pip-version-check',
    'tensorflow-cpu==2.10.1',
    'numpy==1.23.5',
    'h5py==3.7.0',
    'sentencepiece==0.2.0',
    'fastapi==0.115.0',
    'uvicorn==0.30.6',
    'pydantic==2.9.2'
)
& $VenvPython @dependencyArgs
if ($LASTEXITCODE -ne 0) {
    throw 'Installing the local TensorFlow dependencies failed. Check the Python version and available disk space, then rerun this script.'
}

$tempRoot = Join-Path $env:TEMP ("ori-platform-source-" + [Guid]::NewGuid().ToString('N'))
$archivePath = Join-Path $env:TEMP ("ori-platform-" + $OriPlatformCommit + '.zip')
try {
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        Write-Host 'Downloading the pinned Ori Platform source snapshot...' -ForegroundColor Cyan
        Invoke-WebRequest -Uri $SourceArchiveUrl -OutFile $archivePath -UseBasicParsing
    }

    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    Expand-Archive -LiteralPath $archivePath -DestinationPath $tempRoot -Force
    $extractedRoot = Get-ChildItem -LiteralPath $tempRoot -Directory | Select-Object -First 1
    if (-not $extractedRoot) { throw 'The downloaded source archive had no root directory.' }

    $sourceServiceRoot = Join-Path $extractedRoot.FullName 'services\intelligence'
    if (-not (Test-Path -LiteralPath (Join-Path $sourceServiceRoot 'runtime.py') -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $sourceServiceRoot 'tensorflow_core\train_ori.py') -PathType Leaf)) {
        throw 'The downloaded source snapshot is missing the expected Ori TensorFlow runtime files.'
    }

    if (Test-Path -LiteralPath $ServiceRoot) {
        Remove-Item -LiteralPath $ServiceRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $LocalRoot -Force | Out-Null
    Copy-Item -LiteralPath $sourceServiceRoot -Destination $ServiceRoot -Recurse -Force
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$serviceRootFull = [System.IO.Path]::GetFullPath($ServiceRoot)
Push-Location $serviceRootFull
try {
    Write-Host ''
    Write-Host 'Training Ori Core intent model...' -ForegroundColor Cyan
    $coreTrainingArgs = @(
        'tensorflow_core\train_core.py',
        '--output', 'models\ori_core.keras',
        '--epochs', '30',
        '--batch-size', '8',
        '--validation-split', '0.2'
    )
    & $VenvPython @coreTrainingArgs
    if ($LASTEXITCODE -ne 0) { throw 'Ori Core training failed.' }

    Write-Host ''
    Write-Host 'Training Ori language model from the public-domain English corpus and Ori examples...' -ForegroundColor Cyan
    Write-Host 'Training happens on this PC and may take a while. Keep this PowerShell window open.'
    $languageTrainingArgs = @(
        'tensorflow_core\train_ori.py',
        '--data', 'tensorflow_core\data\ori_training_expanded.jsonl',
        '--output', 'artifacts\ori-small',
        '--epochs', '8',
        '--english-epochs', '16',
        '--english-max-chars', '1000000',
        '--batch-size', '16',
        '--validation-split', '0'
    )
    & $VenvPython @languageTrainingArgs
    if ($LASTEXITCODE -ne 0) { throw 'Ori language model training failed.' }
}
finally {
    Pop-Location
}

if (-not (Test-OriPlatformModelInstalled)) {
    throw 'Training returned successfully, but the Ori language model files are incomplete.'
}

if (-not (Test-Path -LiteralPath $LocalKeyPath -PathType Leaf)) {
    $randomBytes = New-Object byte[] 32
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($randomBytes) } finally { $generator.Dispose() }
    $apiKey = [Convert]::ToBase64String($randomBytes)
    [System.IO.File]::WriteAllText($LocalKeyPath, $apiKey, [System.Text.Encoding]::UTF8)
}

$marker = [ordered]@{
    model = 'ori-small'
    framework = 'TensorFlow/Keras'
    sourceRepository = 'https://github.com/30lnewcomb-byte/ori-platform'
    sourceCommit = $OriPlatformCommit
    trainedLocally = $true
    installedAtUtc = [DateTime]::UtcNow.ToString('o')
    note = 'This is a locally trained model from the Ori Platform source pipeline; it is not a copy of the hosted Render checkpoint.'
}
$marker | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $InstallMarker -Encoding utf8

Write-Host ''
Write-Host 'Ori Platform local TensorFlow model installed.' -ForegroundColor Green
Write-Host 'Model files and chat runtime are stored under:' -ForegroundColor Green
Write-Host $LocalRoot
Write-Host 'Mission Control binds the runtime to 127.0.0.1 and sends chat only to this PC.'
Write-Host 'This locally trained compact model may be less capable than Qwen3.5 4B; select either model in Mission Control to compare.'
