<#
.SYNOPSIS
    Скачивает модель Whisper GGML для офлайн-распознавания речи (KI-140).

.DESCRIPTION
    Скачивает модель в IIChatTools.API/tools/whisper/, где её ожидает
    WhisperNetTranscriptionService (через IAppPathProvider.ContentRootPath).

    ВАЖНО: путь намеренно IIChatTools.API/tools/whisper/, а не repo-root/tools/whisper/.
    IAppPathProvider.ContentRootPath = IIChatTools.API (в dev и в Docker /app).
    Speech:ModelPath = "tools/whisper/ggml-base.bin" резолвится относительно него.

.PARAMETER ModelSize
    Размер модели: tiny | base | small | medium | large-v3.
    По умолчанию — base (142 MB, sweet spot для русского).

.PARAMETER Force
    Перезаписать существующий файл.

.EXAMPLE
    pwsh -ExecutionPolicy Bypass -File scripts/setup/download-whisper-model.ps1
    pwsh -ExecutionPolicy Bypass -File scripts/setup/download-whisper-model.ps1 -ModelSize small
#>
param(
    [ValidateSet('tiny','base','small','medium','large-v3')]
    [string]$ModelSize = 'base',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# repo-root = scripts/setup/../..
$repoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path

# Целевая директория — ВНУТРИ IIChatTools.API (ContentRootPath приложения).
# См. комментарий выше: не repo-root/tools, а IIChatTools.API/tools.
$targetDir = Join-Path $repoRoot 'IIChatTools.API\tools\whisper'
$targetFile = Join-Path $targetDir "ggml-$ModelSize.bin"

if (-not (Test-Path $targetDir)) {
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    Write-Host "Создана директория: $targetDir" -ForegroundColor DarkGray
}

if ((Test-Path $targetFile) -and -not $Force) {
    $existingSize = [math]::Round((Get-Item $targetFile).Length / 1MB, 1)
    Write-Host "Модель уже существует: $targetFile ($existingSize MB)" -ForegroundColor Yellow
    Write-Host "Используйте -Force для перезаписи." -ForegroundColor Yellow
    exit 0
}

$sizes = @{ tiny=75; base=142; small=466; medium=1500; 'large-v3'=2900 }
$expectedSize = $sizes[$ModelSize]

$url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-$ModelSize.bin"

Write-Host ""
Write-Host "=== Whisper Model Download ===" -ForegroundColor Cyan
Write-Host "Model size: $ModelSize (~$expectedSize MB)"
Write-Host "URL:        $url"
Write-Host "Target:     $targetFile"
Write-Host ""

$sw = [System.Diagnostics.Stopwatch]::StartNew()
try {
    # Invoke-WebRequest с прогрессом (PowerShell 7 автоматически показывает ProgressBar).
    Invoke-WebRequest -Uri $url -OutFile $targetFile -UseBasicParsing
} catch {
    if (Test-Path $targetFile) { Remove-Item $targetFile -Force }
    Write-Host ""
    Write-Host "Ошибка скачивания: $_" -ForegroundColor Red
    Write-Host "Возможные причины:" -ForegroundColor Yellow
    Write-Host "  - Нет интернета / HuggingFace недоступен"
    Write-Host "  - Прокси блокирует запрос (см. \$env:HTTPS_PROXY)"
    Write-Host "  - Недостаточно места на диске (нужно ~$expectedSize MB)"
    exit 1
}
$sw.Stop()

$size = [math]::Round((Get-Item $targetFile).Length / 1MB, 1)
Write-Host ""
Write-Host "Готово: $targetFile ($size MB за $([math]::Round($sw.Elapsed.TotalSeconds, 1)) сек)" -ForegroundColor Green
Write-Host ""
Write-Host "Перезапустите приложение (dotnet run) — модель загрузится при первом запросе." -ForegroundColor DarkGray