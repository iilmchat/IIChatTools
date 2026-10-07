#Requires -Version 7.0
<#
.SYNOPSIS
    Скачивает tessdata_best для Tesseract OCR (KI-203).
.DESCRIPTION
    Загружает rus.traineddata и eng.traineddata (~40 MB каждый)
    из github.com/tesseract-ocr/tessdata_best.
    Куда: IIChatTools.API/tools/tessdata/
.PARAMETER Languages
    Языки для скачивания. По умолчанию @('rus','eng').
.PARAMETER Force
    Перезаписать существующие файлы.
.EXAMPLE
    pwsh scripts/setup/download-tessdata.ps1
    pwsh scripts/setup/download-tessdata.ps1 -Languages rus
    pwsh scripts/setup/download-tessdata.ps1 -Force
#>
[CmdletBinding()]
param(
    [string[]] $Languages = @('rus', 'eng'),
    [switch]   $Force
)

$ErrorActionPreference = 'Stop'

# Repo root = на 2 уровня выше scripts/setup/.
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$targetDir = Join-Path $repoRoot 'IIChatTools.API\tools\tessdata'

if (-not (Test-Path $targetDir)) {
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Write-Host "[INFO] Создана папка: $targetDir"
}

$baseUrl = 'https://github.com/tesseract-ocr/tessdata_best/raw/main'

foreach ($lang in $Languages) {
    $fileName = "$lang.traineddata"
    $targetPath = Join-Path $targetDir $fileName
    $url = "$baseUrl/$fileName"

    if ((Test-Path $targetPath) -and -not $Force) {
        $sizeMB = [math]::Round((Get-Item $targetPath).Length / 1MB, 1)
        Write-Host "[SKIP] ${fileName} уже есть ($sizeMB MB). Используйте -Force для перезаписи."
        continue
    }

    Write-Host "[GET ] $url"
    try {
        Invoke-WebRequest -Uri $url -OutFile $targetPath -UseBasicParsing
        $sizeMB = [math]::Round((Get-Item $targetPath).Length / 1MB, 1)
        Write-Host "[OK  ] $fileName → $targetPath ($sizeMB MB)"
    }
    catch {
        Write-Error "[FAIL] ${fileName}: $_"
        if (Test-Path $targetPath) { Remove-Item $targetPath -Force }
        throw
    }
}

Write-Host ""
Write-Host "[DONE] tessdata готова: $targetDir"
Write-Host "       Не забудьте включить OCR в appsettings.json:"
Write-Host '       "Rag": { "Ingestion": { "Ocr": { "Enabled": true } } }'