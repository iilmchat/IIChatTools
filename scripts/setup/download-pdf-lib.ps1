<#
.SYNOPSIS
    Скачивает pdf-lib, @pdf-lib/fontkit и TTF-шрифт с кириллицей
    в IIChatTools.API/wwwroot/lib/pdf-lib/ (KI-208).
.DESCRIPTION
    pdf-lib по умолчанию не поддерживает кириллицу (StandardFonts = Latin-1).
    Нужен TTF с Cyrillic + fontkit для регистрации шрифта.

    Fallback-цепочка URL для шрифта:
      1. Roboto Classic (googlefonts/roboto-classic) — статичный TTF.
      2. jsDelivr GH-зеркало того же.
      3. DejaVu Sans (dejavu-fonts) — Apache 2.0, точно с кириллицей.
      4. DejaVu через jsDelivr.

    Если все URL упали — скрипт завершится с предупреждением,
    но pdf-lib/fontkit продолжат работать (graceful fallback в JS).
#>

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $scriptDir)
$target = Join-Path $root 'IIChatTools.API\wwwroot\lib\pdf-lib'

if (-not (Test-Path $target)) {
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Write-Host "[ok] Создана папка: $target"
}

# Каждый файл — массив URL (fallback-цепочка). Пробуем по порядку,
# останавливаемся на первом успешном. Минимальный размер 10 KB —
# защита от HTML-страниц 404, которые CDN иногда отдаёт с кодом 200.
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
        Urls = @(
            # Roboto Classic — статичные хинтованные TTF.
            'https://raw.githubusercontent.com/googlefonts/roboto-classic/main/src/hinted/Roboto-Regular.ttf',
            'https://cdn.jsdelivr.net/gh/googlefonts/roboto-classic@main/src/hinted/Roboto-Regular.ttf',
            # DejaVu Sans — гарантированно с кириллицей (Apache 2.0).
            'https://cdn.jsdelivr.net/gh/dejavu-fonts/dejavu-fonts@version_2_37/ttf/DejaVuSans.ttf',
            'https://raw.githubusercontent.com/dejavu-fonts/dejavu-fonts/version_2_37/ttf/DejaVuSans.ttf',
            # Legacy fallback.
            'https://cdn.jsdelivr.net/npm/dejavu-fonts-ttf@2.37.3/ttf/DejaVuSans.ttf'
        );
    }
)

foreach ($f in $files) {
    $outPath = Join-Path $target $f.Out

    # Проверка: файл уже есть и размер > 10 KB — skip.
    if (Test-Path $outPath) {
        $existingSize = (Get-Item $outPath).Length
        if ($existingSize -gt 10KB) {
            $kb = [math]::Round($existingSize / 1KB)
            Write-Host "  [skip]   $($f.Out) — уже есть ($kb KB)"
            continue
        } else {
            Write-Host "  [clean]  $($f.Out) — размер < 10 KB (мусор), перекачиваем"
            Remove-Item $outPath -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Host "  [get]    $($f.Out) ($($f.Size))..."

    $ok = $false
    foreach ($url in $f.Urls) {
        $shortUrl = $url
        if ($shortUrl.Length -gt 90) { $shortUrl = $shortUrl.Substring(0, 87) + '...' }

        try {
            Invoke-WebRequest -Uri $url -OutFile $outPath -UseBasicParsing `
                -ErrorAction Stop -TimeoutSec 30

            $actualSize = (Get-Item $outPath).Length
            if ($actualSize -lt 10KB) {
                Write-Host "           skip (< 10 KB — мусор)"
                Remove-Item $outPath -Force -ErrorAction SilentlyContinue
                continue
            }

            $kb = [math]::Round($actualSize / 1KB)
            Write-Host "           OK ($kb KB)"
            Write-Host "           url: $shortUrl"
            $ok = $true
            break
        } catch {
            Write-Host "           fail: $shortUrl"
        }
    }

    if (-not $ok) {
        Write-Warning "           все URL недоступны для $($f.Out)."
        Write-Warning "           PDF-экспорт будет работать без кириллицы (fallback Helvetica)."
    }
}

Write-Host ''
Write-Host "Готово. Файлы в: $target"
Write-Host 'Проверка:'
Write-Host '  Get-ChildItem IIChatTools.API\wwwroot\lib\pdf-lib\'
Write-Host ''
Write-Host 'Ожидание: 3 файла (pdf-lib.min.js, fontkit.umd.min.js, Roboto-Regular.ttf).'