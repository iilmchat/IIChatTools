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

$# Каждый файл — массив URL (fallback-цепочка). Пробуем по порядку,
# останавливаемся на первом успешном.
$files = @(
    @{
        Out = 'pdf-lib.min.js';
        Size = '~220 KB';
        Urls = @(
            'https://unpkg.com/pdf-lib@1.17.1/dist/pdf-lib.min.js',
            'https://cdn.jsdelivr.net/npm/pdf-lib@1.17.1/dist/pdf-lib.min.js'
        );
    },
    @{
        Out = 'fontkit.umd.min.js';
        Size = '~120 KB';
        Urls = @(
            'https://unpkg.com/@pdf-lib/fontkit@1.1.1/dist/fontkit.umd.min.js',
            'https://cdn.jsdelivr.net/npm/@pdf-lib/fontkit@1.1.1/dist/fontkit.umd.min.js'
        );
    },
    @{
        Out = 'Roboto-Regular.ttf';
        Size = '~170 KB';
        # KI-208-fix: Google Fonts убрал статичные TTF Roboto из main/ofl.
        # Fallback-цепочка:
        #   1. Roboto Classic (repo googlefonts/roboto-classic, актуален);
        #   2. DejaVu Sans (гарантированно с кириллицей, Apache 2.0);
        #   3. Liberation Sans (тоже с кириллицей).
        Urls = @(
            'https://raw.githubusercontent.com/googlefonts/roboto-classic/main/src/hinted/Roboto-Regular.ttf',
            'https://cdn.jsdelivr.net/gh/googlefonts/roboto-classic@main/src/hinted/Roboto-Regular.ttf',
            'https://cdn.jsdelivr.net/npm/dejavu-fonts-ttf@2.37.3/ttf/DejaVuSans.ttf',
            'https://raw.githubusercontent.com/dejavu-fonts/dejavu-fonts/master/ttf/DejaVuSans.ttf'
        );
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
    $ok = $false
    foreach ($url in $f.Urls) {
        try {
            Invoke-WebRequest -Uri $url -OutFile $outPath -UseBasicParsing -ErrorAction Stop
            $actualSize = [math]::Round((Get-Item $outPath).Length / 1KB)
            if ($actualSize -lt 10) {
                # Мусорный ответ (HTML-страница 404 с CDN).
                Remove-Item $outPath -Force -ErrorAction SilentlyContinue
                Write-Host "           skip (мусорный ответ < 10 KB)"
                continue
            }
            Write-Host "           OK ($actualSize KB) from $url"
            $ok = $true
            break
        } catch {
            Write-Host "           fail: $url"
        }
    }

    if (-not $ok) {
        Write-Warning "           все URL недоступны для $($f.Out)"
    }
}

Write-Host ''
Write-Host "Готово. Файлы в: $target"
Write-Host 'Проверка: ls IIChatTools.API\wwwroot\lib\pdf-lib\'