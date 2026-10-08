<#
.SYNOPSIS
    Скачивает pdf-lib, @pdf-lib/fontkit и Roboto Regular TTF
    в IIChatTools.API/wwwroot/lib/pdf-lib/ (KI-208).
.DESCRIPTION
    pdf-lib по умолчанию не поддерживает кириллицу (StandardFonts = Latin-1).
    Нужен TTF с Cyrillic (Roboto Regular) + fontkit для регистрации шрифта.
    Все файлы кладутся локально, offline-first.
#>

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $scriptDir)
$target = Join-Path $root 'IIChatTools.API\wwwroot\lib\pdf-lib'

if (-not (Test-Path $target)) {
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Write-Host "[ok] Создана папка: $target"
}

$files = @(
    @{
        Url = 'https://unpkg.com/pdf-lib@1.17.1/dist/pdf-lib.min.js';
        Out = 'pdf-lib.min.js';
        Size = '~220 KB';
    },
    @{
        Url = 'https://unpkg.com/@pdf-lib/fontkit@1.1.1/dist/fontkit.umd.min.js';
        Out = 'fontkit.umd.min.js';
        Size = '~120 KB';
    },
    @{
        Url = 'https://raw.githubusercontent.com/google/fonts/main/ofl/roboto/Roboto-Regular.ttf';
        Out = 'Roboto-Regular.ttf';
        Size = '~170 KB';
    }
)

foreach ($f in $files) {
    $outPath = Join-Path $target $f.Out
    if (Test-Path $outPath) {
        $actualSize = [math]::Round((Get-Item $outPath).Length / 1KB)
        Write-Host "  [skip]   $($f.Out) — уже есть ($actualSize KB)"
        continue
    }

    Write-Host "  [get]    $($f.Out) ($($f.Size))..."
    try {
        Invoke-WebRequest -Uri $f.Url -OutFile $outPath -UseBasicParsing
        $actualSize = [math]::Round((Get-Item $outPath).Length / 1KB)
        Write-Host "           OK ($actualSize KB)"
    } catch {
        Write-Warning "           FAIL: $_"
    }
}

Write-Host ''
Write-Host "Готово. Файлы в: $target"
Write-Host 'Проверка: ls IIChatTools.API\wwwroot\lib\pdf-lib\'