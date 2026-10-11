<#
.SYNOPSIS
    Скачивает Mermaid + KaTeX (UMD-бандлы + шрифты) в wwwroot/lib/.

.DESCRIPTION
    KI-232 (v1.13.15): рендеринг Mermaid-диаграмм и KaTeX-формул в чате.
    Скачивает с cdnjs.cloudflare.com:
    - mermaid.min.js (UMD, ~2.5 MB)
    - katex.min.js + katex.min.css (~280 KB + 23 KB)
    - 20+ шрифтов KaTeX (WOFF2, ~1 MB)

    Идемпотентный: пропускает уже скачанные файлы (флаг -Force для перезаписи).
    По образцу download-pdf-lib.ps1 (KI-208).

.PARAMETER Force
    Перезаписать существующие файлы.

.PARAMETER Version
    Версии библиотек. По умолчанию: mermaid 11.4.1, katex 0.16.11.

.EXAMPLE
    pwsh -ExecutionPolicy Bypass -File scripts/setup/download-mermaid-katex.ps1

.EXAMPLE
    pwsh -ExecutionPolicy Bypass -File scripts/setup/download-mermaid-katex.ps1 -Force
#>

[CmdletBinding()]
param(
    [switch]$Force,
    [string]$MermaidVersion = "11.4.1",
    [string]$KatexVersion = "0.16.11"
)

$ErrorActionPreference = "Stop"

# --- Пути ---
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$libMermaid = Join-Path $repoRoot "IIChatTools.API\wwwroot\lib\mermaid"
$libKatex = Join-Path $repoRoot "IIChatTools.API\wwwroot\lib\katex"
$katexFonts = Join-Path $libKatex "fonts"

Write-Host "[download] repoRoot = $repoRoot" -ForegroundColor Cyan
Write-Host "[download] mermaid  = $MermaidVersion"
Write-Host "[download] katex    = $KatexVersion"
Write-Host ""

# --- Создаём папки ---
foreach ($dir in @($libMermaid, $libKatex, $katexFonts)) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "[mkdir] $dir" -ForegroundColor DarkGray
    }
}

# --- Helper: скачать файл (с проверкой существования) ---
function Get-LibFile {
    param(
        [Parameter(Mandatory)] [string]$Url,
        [Parameter(Mandatory)] [string]$OutFile
    )

    $fileName = Split-Path $OutFile -Leaf

    if ((Test-Path $OutFile) -and -not $Force) {
        Write-Host "[skip]  $fileName (уже есть)" -ForegroundColor DarkGray
        return
    }

    Write-Host "[get]   $fileName" -ForegroundColor Yellow

    try {
        Invoke-WebRequest -Uri $Url -OutFile $OutFile -UseBasicParsing
    }
    catch {
        Write-Host "[error] Не удалось скачать $Url" -ForegroundColor Red
        Write-Host "        $($_.Exception.Message)" -ForegroundColor Red
        throw
    }
}

# --- 1. Mermaid ---
$mermaidBase = "https://cdnjs.cloudflare.com/ajax/libs/mermaid/$MermaidVersion"
Get-LibFile `
    -Url "$mermaidBase/mermaid.min.js" `
    -OutFile (Join-Path $libMermaid "mermaid.min.js")

# --- 2. KaTeX (js + css) ---
$katexBase = "https://cdnjs.cloudflare.com/ajax/libs/KaTeX/$KatexVersion"
Get-LibFile `
    -Url "$katexBase/katex.min.js" `
    -OutFile (Join-Path $libKatex "katex.min.js")
Get-LibFile `
    -Url "$katexBase/katex.min.css" `
    -OutFile (Join-Path $libKatex "katex.min.css")

# --- 3. KaTeX fonts (WOFF2) ---
# Список всех шрифтов KaTeX 0.16.x — 20 файлов.
# Источник: https://github.com/KaTeX/KaTeX/tree/main/dist/fonts
$katexFontFiles = @(
    "KaTeX_AMS-Regular.woff2",
    "KaTeX_Caligraphic-Bold.woff2",
    "KaTeX_Caligraphic-Regular.woff2",
    "KaTeX_Fraktur-Bold.woff2",
    "KaTeX_Fraktur-Regular.woff2",
    "KaTeX_Main-Bold.woff2",
    "KaTeX_Main-BoldItalic.woff2",
    "KaTeX_Main-Italic.woff2",
    "KaTeX_Main-Regular.woff2",
    "KaTeX_Math-BoldItalic.woff2",
    "KaTeX_Math-Italic.woff2",
    "KaTeX_SansSerif-Bold.woff2",
    "KaTeX_SansSerif-Italic.woff2",
    "KaTeX_SansSerif-Regular.woff2",
    "KaTeX_Script-Regular.woff2",
    "KaTeX_Size1-Regular.woff2",
    "KaTeX_Size2-Regular.woff2",
    "KaTeX_Size3-Regular.woff2",
    "KaTeX_Size4-Regular.woff2",
    "KaTeX_Typewriter-Regular.woff2"
)

Write-Host ""
Write-Host "[fonts] Скачиваю $($katexFontFiles.Count) шрифтов KaTeX..." -ForegroundColor Cyan

foreach ($font in $katexFontFiles) {
    Get-LibFile `
        -Url "$katexBase/fonts/$font" `
        -OutFile (Join-Path $katexFonts $font)
}

# --- Итог ---
Write-Host ""
Write-Host "=== Готово ===" -ForegroundColor Green
Write-Host "Mermaid: $libMermaid"
Write-Host "KaTeX:   $libKatex"
Write-Host ""
Write-Host "Проверьте: wwwroot/lib/mermaid/mermaid.min.js и wwwroot/lib/katex/katex.min.js"
Write-Host "Дальше — Ctrl+Shift+R в браузере, чтобы сбросить кэш."